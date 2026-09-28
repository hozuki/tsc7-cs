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

**CLI 前端是一个独立工程**（用户要求：`tsc[.exe]` 与 `TypeScript.Compiler` 分开成两个工程，与 Go 的结构一致）：

| TSGO 路径 | C# 落点 | 说明 |
| --- | --- | --- |
| `tsc/cmd/tsc/**`（main.go、sys.go、lsp.go、api.go、平台文件，519 行） | **`src/tsc/tsc.csproj`**（`OutputType=Exe`、`AssemblyName=tsc`、`RootNamespace=TypeScript`、`IsPackable=false`；文件命名空间 `TypeScript.Tsc`） | **进程级**的 CLI 前端：读 `osutil.Args()`、`--lsp`/`--api` 分派、真实 stdout/stderr/环境/cwd/`vfs.FS`、`spawnProcess`、信号与大栈线程。Go 的 `package main` 没有包名，故命名空间取目录名（`tsc`）。 |
| `tsc/internal/execute/**` | `src/TypeScript.Compiler/Execute/`（`TypeScript.Execute`） | CLI 的**逻辑**（`CommandLine`/`TscCompilation`/`build`/`watch`/`statistics`/`Pprof`…）：Go 里它是**可 import 的库 package**，所以**留在库里不动**（`--version`/`--help`/编译/`--init` 的实现在这里）。 |

两者的边界只走**公开 API**（`Execute.CommandLine`、`Tsc.System`/`ExitStatus`、`Bundled.WrapFS`/`LibPath`、`Osvfs.FS`/`GetGlobalTypingsCacheLocation`、`Lsp.Server`、`Api.StdioServer`、`Pprof.BeginProfiling`、`ContentMapper.Spawner`/`IProcessExitState`），**没有**用到任何 `internal` 成员 ⇒ 库里**不需要** `InternalsVisibleTo("tsc")`（`Properties/AssemblyInfo.cs` 里那两条 IVT 只服务测试与 harness）。三条只在 CLI 侧成立的约定：主流程跑在显式 64 MiB 栈线程上（§3.9）；stdout/stderr 的 `NewLine` 固定 `"\n"` + UTF-8（Go 的 `fmt.Fprintln` 恒 LF，而 .NET 在 Windows 上默认 CRLF，逐字节对拍会全红）；`System.Diagnostics` 只以别名引入（§2 的 `Debug.Fail` 坑）。

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
- **大文件拆分（用户批准）**：Go 里单个超大文件（`parser/parser.go` 6854 行、`ast/utilities.go` 4631 行、`ast/ast.go` 3189 行…）允许按**逻辑段落**拆成多个 C# 文件，用 `partial` 类型落地：
  * 同一个 Go 类型/静态类 → **同一个** C# 类型（例如 `Parser` 的所有部分都是 `partial class Parser`；包级函数进 `partial class Parser`/`Ast`）；
  * 文件名 `<GoFilePascal>.<逻辑段>.cs`（如 `Parser.Declarations.cs`、`Parser.Expressions.cs`、`Ast.Utilities.Symbols.cs`），段落划分要能说清（声明/语句/表达式/类型/JSX/JSDoc…）；
  * 每个分片的**首行写清来源与行范围**：`// ported from: internal/parser/parser.go:1234-2345（声明：类/接口/枚举/函数声明）`；
  * 不允许跨分片重复定义成员，也不允许把同一段 Go 代码拆到两个分片；
  * 合并时**分片顺序不影响语义**（C# partial 的类型成员集合与顺序无关；但 Go 里依赖文件内顺序的地方——几乎没有——要照原顺序）。
- **命名（用户要求）**：**公开 API 与 TSGO 一致**——Go 导出成员的名字原样保留（本来就是 PascalCase：`Identifier.Text`、`NodeFactory.NewIdentifier`、`AsIdentifier`、`Kind`）；**非公开成员一律按 C# 习惯命名**，不要照抄 Go 的小写名：
  * 实例字段：**Go 未导出**字段一律 `_camelCase`——只在本类用 `private`，需要被同程序集其它类/其它 `partial` 文件访问用 `internal`（**仍带 `_`**）。**Go 导出**字段用 `internal`/`public` + PascalCase **原名、不加前缀**（它是要镜像的公开 API：`ResolvedModule.Path` → `internal string Path = "";`）。const 任何可见性都用 PascalCase。**不要**用 `Name_`/尾下划线这种"照抄 Go 的避让写法"——TSGO 里根本没有这种名字；Go 的 `name` 是未导出字段，公开访问走 `Name()` 方法，C# 里就是 `_name` + `Name()`。
  * 方法/属性（任何可见性）：PascalCase（Go 的 `setModifiers` → `SetModifiers`，`computeSubtreeFacts` → `ComputeSubtreeFacts`）。
  * 局部变量/参数：camelCase。
  * 未导出类型（如 Go 的 `nodeData`）：按 C# 习惯命名，不必与 Go 同名（接口用 `I` 前缀）。导出类型名照抄。
  * 需要被同程序集其它类访问的 Go 未导出成员 → `internal`（仍是 `_camelCase` 字段 / PascalCase 方法）；只在本类用 → `private`。
- 命名空间必须显式声明为 file-scoped（`.editorconfig` 已把它设为 warning 级）。
- 命名规范由 `.editorconfig` 的命名规则强制（`_camelCase` 字段等），新增/改动代码必须过。
- **禁止 `using System.Diagnostics;`**（Vfs 实测的坑）：文件级 using 属于最内层命名空间声明，名字解析先走它，于是 `Debug.Fail(...)` 会绑到 **`System.Diagnostics.Debug.Fail`** —— 它不是我们的 `TypeScript.Debug.Fail`：VSTest 下抛 `DebugAssertException`、普通进程里**直接 FailFast 终止进程**（`Assert.Throws<DebugFailureException>` 永不成立）。要 `Stopwatch`/`UnreachableException` 就用别名（`using Stopwatch = System.Diagnostics.Stopwatch;`）。`.cs` 里出现 `TypeScript.Debug.Fail` 全名也可以，但别名更省事。
- **公开 API 对等检查**（周期跑，落实"公开 API 与 TSGO 一致"）：
  ```sh
  dotnet run --project artifacts/api-parity            # 反射 dump 我们产物的 public API
  node artifacts/api-parity/compare.mjs               # 与 Go 的导出声明比对
  ```
  当前：**通过 2865 项 / 缺失 0 / 白名单 15 项（逐条给理由，如 §3.1 不移植的哨兵函数、Go 类型别名 → C# enum/直接类型、`(T,bool)` → `TryX`、`base` 结构体 → `I<Base>` 接口）**。
  它没有做成永久测试的原因：dump 需要反射，而测试程序集开了 AOT 分析器（IL2026/IL2075 会因 TreatWarningsAsErrors 变错），所以放在 `artifacts/`（gitignore）里按需运行；TSGO 检出与 `TSGO_ROOT` 是前提。

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
| `Arena[T].NewSlice/NewSlice1/Clone` | 返回 `T[]`（Go 调用点会把结果存进字段，`Span<T>` 存不了）。**偏离**：C# 数组无法是"从 slab 切出的视图"，这三个方法退化成普通分配，只有 `ref T New()` 保留 slab 语义。Go 里 `arena.NewSlice(n)[:0]` + append 的预占容量写法（parser/binder/printer 有多处）在 C# 要改成 `List<T>`/数组 + 长度计数器。 |
| `LinkStore[K,V]` / `PagedLinkStore[V]` 的 `*V` | `where V : class, new()` + `new V()`；`null` = Go 的 `*V == nil`（`Has`/`TryGet` 语义完全一致）。Go 里 V 常是结构体（紧凑存储），C# 移植成 class：每个槽位一次分配、页是引用数组（性能偏离，待实测）。 |
| Go 用 `reflect` 做**浅拷贝**（如 `CompilerOptions.Clone` 逐字段 `reflect.Value.Set`，Go 源码里自己标了 "could be generated code instead of reflection"） | `MemberwiseClone()`：同样是逐字段浅拷贝、无反射、AOT 安全，不需要生成 150 行赋值 |
| `iota` 枚举 | C# `enum`，**数值与 TS/Go 保持一致**（`ast.Kind` 等值会出现在 baseline 里） |
| Go 的无类型常量（`n * 2`、`n < 0`） | `Number` 定义 `implicit` 的 `double/int/long/float → Number`（读起来与 Go 一致）；反方向仍是 `explicit`（对应 Go 的 `float64(n)`）。`Number` 还实现了 `System.Numerics` 的运算符接口族（`IAdditionOperators`/`ISubtractionOperators`/…/`IComparisonOperators`/`IAdditiveIdentity`），可被泛型算法消费；**不**实现 `INumberBase` 那一套（`Parse`/`TryConvert*` 的 .NET 契约与 JS 的 `Number(string)`/`ToInt32` 语义冲突） |
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

### 3.8 Span 优先（性能硬要求，AGENTS.md）

Go 版本大量用**切片视图**（`s[i:j]`、`bytes`、`[]T` 的子切片）做到零拷贝；C# 的对应物是 `Span<T>`/`ReadOnlySpan<T>`。移植时必须同样使用，别用会分配/拷贝的等价物：

| Go | C#（优先） | 反面（除非有理由） |
| --- | --- | --- |
| `s[i:j]` | `s.AsSpan(i, j-i)` | `s.Substring(i, j-i)` |
| 接收 `[]T`/`string` 只读扫描 | `ReadOnlySpan<T>` 参数（或 `string` + `AsSpan`） | 先 `ToArray()`/拷贝 |
| 写入/就地修改 `[]T` | `Span<T>` 参数 | 先拷贝再回写 |
| 遍历 `List<T>` 热路径 | `CollectionsMarshal.AsSpan(list)`、索引循环 | `foreach` + LINQ |
| `copy(dst, src)` | `src.CopyTo(dst)` / `dst.CopyTo(src)` | 手写逐元素循环（除非要自定义语义） |
| 拼接/构造长字符串 | `string.Create`/`StringBuilder` + `CopyTo` | 大量 `+`/`Substring` |
| `append` 预占容量 | `List<T>`（C# 数组没有 len<cap 视图） | — |

限制（必须记住）：`Span<T>`/`ReadOnlySpan<T>` **不能存进字段、不能跨 `await`、不能在迭代器里存活**；这类地方用 `T[]`（§3.2 的 `Arena`/`LinkStore` 条目已经处理了这一点）。生成的代码同样遵守：需要长期持有的数据用数组，只在方法内部做零拷贝视图。

审计要求：热路径（scanner/parser/binder/checker/printer 以及所有逐字符/逐元素循环）里出现 `Substring`/`ToArray`/`ToCharArray`/`Encoding.*GetBytes`/LINQ 时必须能给出理由，否则改成 span 写法。

#### 3.8.1 读取路径不物化 `string`（实测契约）

**约定（本仓字符串/span 的铁律）**：**读取用 `ReadOnlySpan<char>`（≈ Go 的 `string` 切片），只在必须建新对象
（存入 AST/缓存/返回给外部调用方/进诊断参数）时才物化 `string`。**

Go 的 `s[a:b]` 是**零拷贝**的（string 头 + 原底层数组），所以 TSGO 里满屏的 `s.text[a:b]`、
`commentText[i+1:]`、`TokenText()` 都**不分配**；C# 的 `string` 不能做视图，同样的写法每次都会 `Substring`（O(len) 复制）。
必须在 C# 侧也写成"读用 span、存才物化"，否则一个 O(1) 的切片会变成 O(n) 的 memcpy。

**扫描窗口（Go 的 `s.text[a:b]` 的等价物）**：Go 的 `s.text` 与 `s.end` 是两个字段，调用方可以只把扫描限制在
`[0, end)`（`s.SetText(text[:end])`，scanner.go:396-400）。C# 的 `string` 没法截断成视图，所以：

| Go | C# | 说明 |
| --- | --- | --- |
| `s.text` | `Scanner.Text()`（全文 string） | `_text` 永远是**全文**，从不复制成子串 |
| `s.SetText(text[:end])` | `Scanner.SetText(string text, int end)`（internal） | 只收窄 `_end`，零拷贝 |
| `len(s.text)` | `_end`（不是 `_text.Length`） | 收窄后所有边界判断必须用 `_end` |
| `s.text[pos:]` | `Scanner.TextFrom(int pos)`（internal，`_text.AsSpan(pos, _end - pos)`） | 以 `_end` 为界的零拷贝视图 |
| `s.text[s.tokenStart:s.pos]` | `Scanner.TokenTextSpan()`（public） | `TokenText()` 是物化版（保留给"存起来"的调用点） |

`ReadOnlySpan<char>` 是 ref struct：**不得存字段、不得跨 `await`/`yield`**；span 本身可以在同一次调用里随便切
（切片只移动 start/length；`_text` 是 immutable 的，后续 `NextToken()` 不会让已有 span 失效）。

**必须仍然是 `string` 的地方（刻意保留，不是漏改）**：

- AST 上存文本的字段（`Identifier.Text`、`TemplateHead.RawText`…）——Go 侧（`ast.Node.Text string`）同样物化；
- `Scanner.TokenValue()`（Go 的 `s.tokenValue` 也是 string）、`Scanner.ScanIdentifier` 的 `_tokenValue = _text[start.._pos]`
  ——值会被 parser 拿进 AST；Go 的切片在这里是"pre-materialized"，C# 没有等价物；
- parser 的 **scratch 集合**（`_jsdocCommentsSpace: List<string>`、`ParseTagComments` 的 `comments`/`indents`/JSDoc link 的 `text`）
  ——Go 侧同样是 `[]string`（只能存 string），且后续还要 `RemoveLeadingNewlines`/`TrimEnd` 就地改元素；
- 诊断参数（`ErrorCallback` 的 `params object?[]`）——必须是 object/string。

**本次实测（`artifacts/span-audit`，2026-09-28，Release，305 文件 / 2.72 MB 语料，`-n 5`，分配字节稳定 ±0.1%）**：

逐阶段（每批改动都在同一 instrument 上复测；`alloc B/char` 是"该相位分配字节 ÷ 语料字符数 ÷ reps"）：

| 阶段 | scan B/char | parse B/char | 说明（改了什么） |
| --- | --- | --- | --- |
| 基线 | 3.999 | 11.280 | — |
| + 批 1 | 2.106 | 9.838 | `Scanner.ScanJSDocCommentForTags/_text[...]` 与 `ScanTemplateAndSetTokenValue` 同时落地 |
| + 批 2 | **1.945** | 9.836 | `ScanTemplateAndSetTokenValue` 无转义时走单段直达（不建 parts 表） |
| + 批 3 | 1.945 | **9.777** | `GetTemplateLiteralRawText` 用 `TokenTextSpan()`（1 次物化而非 2 次） |

批 1 的两条改动各自对应一条 Go 依据（都在 `artifacts/span-audit/logs/*.nettrace` 的调用栈归因里）：
`Scanner.scanJSDocCommentForTags` 收到的是 `s.text[s.tokenStart:s.pos]` **切片**（scanner.go:679），循环里
`commentText[i+1:]` 也是切片（scanner.go:364）——C# 原来是"整段注释 `Substring` 一次 + 每个 `@` 再 `Substring` 一次"；
`scanTemplateAndSetTokenValue` 是 `parts []string` + `strings.Join(parts, "")`（scanner.go:1637-1682，**一次分配**）
——C# 原来是 `StringBuilder` 的多次 `ExpandByABlock` + `ToString`（trace 里占 scan 相位分配的 51%）。

**净效果**：scan **-51.3%**（3.999 → 1.945 B/char，51.9 → 25.3 MB / 5 reps），parse **-13.3%**（11.280 → 9.777 B/char，
146.4 → 127.0 MB / 5 reps）。JSDoc 懒解析路径（parse + 全树 `node.JSDoc(file)`，即 Api/Encoder 场景）：
**-3.1%**（15.359 → 14.875 B/char，119.6 → 115.9 MB，批 4 的 `asterisk`/`whitespace` 局部量 span 化，
用 A/B 单变量测得）。

参考基线（同批未改的相位）：bind 2.812 B/char、print 23.140 B/char（printer 当时不在文件范围内，仅记录；
已在 §3.8.1.1 单独处理）。

**各相位总览（同一仪器、同一语料 305 文件 / 2.72 M 字符 / `-n 5`；"第一批前"= 两轮改动之前的基线）**：

| 相位 | 第一批前 | 现在 | 变化 |
| --- | --- | --- | --- |
| scan | 3.999 | **1.945** | −51.4% |
| parse | 11.280 | **7.403**（第三批，见 §3.8.1.2） | −34.4% |
| jsdoc（parse + 全树 `node.JSDoc(file)`；与 parse 重叠，不计入合计） | 15.359 | **14.875** | −3.2% |
| print | 23.140 | **18.14–18.59（中位 18.30）** | −20.9% |
| bind | 2.812 | 2.812 | 0（未动） |
| **四相位合计（scan+parse+bind+print）** | **41.231** | **30.46**（1.945 + 7.403 + 2.812 + 18.30） | **−26.1%** |

**同批测量后判定"不值得改/不能改"的项（如实记录）**：

| 热点 | 依据 | 结论 |
| --- | --- | --- |
| `Scanner.ScanIdentifier` 的 `_tokenValue = _text[start.._pos]`（parse 相位 8.6%） | Go `scanner.go:1497/1515` 同样是切片，但值是 parser 要存进 AST 的 `tokenValue` | 物化不可避免（除非把 `tokenValue` 改成"start/end + 懒物化"，会动 `ScannerState`/`Rewind`/所有 `_tokenValue` 写入点，风险 >> 收益） |
| `Parser.ParseModifiersEx` 的 `new List<Node>(16)`（parse 相位当时 16%） | Go `parser.go:3921` `make([]*ast.Node, 0, 16)` 恒分配 128 B；C# 的 `List<Node>(16)` 是 192 B（此前的"40 B 反而更省"是**误判**，已更正） | **第三批处理**（改成首次 append 才建表，−1.74 B/char）⇒ 见 §3.8.1.2 |
| `ParserJsDoc` 里 15 处存进 `List<string>` 的 `TokenText()` | Go 侧 `comments []string` 也是存 string；且 `RemoveLeadingNewlines`/`RemoveTrailingWhitespace` 要就地改元素 | 必须物化（要改就得把集合换成 `(start,len)` 视图并重写这些 helper，收益/风险不划算） |
| printer 的 `StringBuilder.ExpandByABlock`（print 相位 23.3 B/char，全局最大） | — | 不在第一批文件范围（`Printer/**`）⇒ 见下 §3.8.1.1（已单独一轮处理：**23.140 → 18.30 B/char（中位），−20.9%**） |

#### 3.8.1.1 Printer 注释写出 / 缩进 / 列表范围（实测，2026-09-28 第二批）

`Printer/**` 是本仓**单一最大分配者**（print 相位 ≈ 300 MB / 5 reps = 23.140 B/char，占总量一半以上）。本批沿用
上一批的方法学（`artifacts/span-audit` 的 `phase print`/`phases` 精确分配字节 + `artifacts/span-allocreport` 的
完整调用链归因，`dotnet-trace --profile gc-verbose` 采样），**逐条 A/B 单变量复测**后才保留。

**调用链归因（`artifacts/span-audit/logs/print.nettrace`，30 reps，按 `TypeScript.Printer.` 过滤，283
ticks/rep 量级；字节数与"整进程采样和"≈ 精确计数器的 print 相位总量，误差 < 0.1%）**：

```
19.5%  Printer.EmitLeadingComments（内联的 List<CommentRange> 构造 —— 每个节点一次）
 9.2%  StringBuilder.set_Capacity ← Printer.Write            （首次 Grow(len(text)) 的容量分配，Go 同款）
 9.0%  String.Ctor ← EmitComment / ← ReadOnlySpan.ToString ← WriteCommentRangeWorker（注释文本物化）
 5.2%  Scanner.IterateCommentRanges → （迭代器 + 每条注释一个 CommentRange 对象，见"不改"表）
 5.1%  Printer.EmitListItems / 3.0% EmitListRange             （列表范围切片复制）
 4.5%  Printer.EmitTrailingComments（同 EmitLeadingComments）
 2.5%  Printer.GetIndentString（每次换行 new string(' ', n)）
 3.6%  NameGenerator.PushScope                                （Go 也每次 new nameGenerationScope，见"不改"表）
```

**已改（每条都有 Go 依据 + A/B 数字；`phases both 300 0 -n 5`，B/char 三次一致才认）**：

| # | 改动 | Go 依据 | A/B 收益（print B/char） |
| --- | --- | --- | --- |
| 1 | 注释列表**懒分配**：`EmitLeadingComments`/`EmitTrailingComments`/`EmitTrailingCommentsOfPosition`/`TryEmitDetachedComments` 由无条件 `new List<CommentRange>()` 改成 `(comments ??= new List<CommentRange>()).Add(...)`，`EmitComments` 收 `List<CommentRange>?` | `printer.go:5563/5613/5632/5686` 都是 `var comments []ast.CommentRange`（nil 切片 + append）：没注释可写时**零分配** | 单点回退 `EmitLeadingComments` 一处即 **+1.29**（19.98/19.84 vs 18.67/18.68）；其余三处未逐个隔离，见下方"合计" |
| 2 | `WriteCommentRangeWorker` 不再物化注释文本：`currentLineText.ToString()` → 直接传 span；单行注释 `text[a..b]` → `text.AsSpan(a,…)` | `printer.go:719` `strings.TrimSpace(text[pos:end])` 是**切片**、`:734` `p.writeComment(text[loc.Pos():loc.End()])` 也是切片 | **−1.22**（两处写法同时回退：19.838 vs 18.62） |
| 3 | 缩进串缓存：`TextWriter` 内置按需增长的 `' '` 串，`WriteText` 走 `Append(spaces,0,n)`、注释对齐走 `RawWriteSpaces(n)`（删掉包级 `getIndentString` 的每次 `new string`） | Go 的 `getIndentString` 源码里自己写着 `TODO: This is cached in tsc`（`textwriter.go:135` 附近），我们照做 | **−0.30…−0.60**（两次：18.92/19.22 vs 18.52/18.62；中位 −0.45） |
| 4 | `EmitListItems` 签名改成 `(Node[] children, int start, int count)`，调用点不再 `nodes[start..end]` 复制数组 | Go `printer.go:4802` 传的是 `children.Nodes[start:end]` **切片视图**；`emitModifierList`（`printer.go:2262` 附近）同理 | **−0.47**（回退为单次复制：31.39 vs 30.92，两批各 5 次采样区间不重叠） |

**净效果：print 23.140 → 18.14 / 18.30 / 18.59 B/char（三次，中位 **18.30**，−20.9%；300.4 → 235.5–241.4 MB / 5 reps）**。
四项单独回退测得 −1.29/−1.22/−0.45/−0.47（合计 −3.43），与总量 −4.84 的差 ≈ −1.4 B/char 落在第 1 项**未逐个隔离**的
另三处懒分配（`EmitTrailingComments`/`EmitTrailingCommentsOfPosition`/`TryEmitDetachedComments`）上。
**scan/parse/bind 未变**（1.945 / 9.83 / 2.812，与上一轮一致）。同一改动在 `phase print`（parse+bind+print）模式下是
**35.775 → 30.80–30.97 B/char**（±0.4% 重复性），与 print 相位 −20.9% 自洽。

> 仪器说明：`phases` 模式三次采样的重复性是 ±1.2%（不是上一轮记录的 ±0.1% —— 那是同一批连续采样的口径）；
> 判读收益时以"两批采样区间是否重叠"为准（第 3、4 项就是这样定论的），只有区间分离才认。

**支撑改动（为让 span 写路径成立，行为逐字节等价）**：

- `TextWriter.lastWritten` 字段删除，`HasTrailingWhitespace()` 改读 `_builder[^1]`（`TextWriter.cs` 文件头第 5 条
  给出不变量证明：三个写入点都"写什么就把什么记进 lastWritten"，`Clear` 同时清空二者 ⇒ builder 非空时末位代码单元恒等）。
  覆盖：`compiler` 套件 3 批（offset 0/3000/6000）共 9,539 条 baseline **逐字节一致**，另单独跑
  `numericLiteralsWithTrailingDecimalPoints01/02.ts`、`toStringOnPrimitives.ts` 等 12 个会走
  `MayNeedDotDotForPropertyAccess` + `HasTrailingWhitespace` 的用例（38 条 baseline 逐字节一致）。
- 新增 `ISpanTextWriter`（internal，C# 专用，对应 `IGrowableTextWriter` 的手法）：`TextWriter` 实现、
  `ChangeTrackerWriter`/`TrailingSemicolonDeferringWriter` 转发；不支持的写者回落 `WriteComment(string)`（仅多一次物化）。

**本批测过但判定"不改"的项（如实记录）**：

| 热点 | 依据 | 结论 |
| --- | --- | --- |
| `StringBuilder.set_Capacity ← Printer.Write`（trace 里 155 MB/30 reps） | `Printer.Write` 调 `Grow(len(sourceFile.Text()))`＝Go `printer.go:5099` 的同名调用 | **Go 同款**：都是"按源文件长度预留一次 + 后续翻倍增长"（.NET 翻倍 vs Go 1.25×，量级相当）。不是移植偏差，未动 |
| `StringBuilder.ToString ← TextWriter.String`（1.9 MB/rep） | Go 的 `strings.Builder.String()` **零拷贝**（buffer 直接转 string），C# 的 `StringBuilder.ToString()` 必然复制一次 | C# 无等价物（要零拷贝就得自己管 `char[]`），且这是**产物本身**，未动 |
| `Scanner.IterateCommentRanges`（print 期间 2.7 MB/rep：迭代器对象 + 每条注释一个 `CommentRange` 对象） | Go 是 `iter.Seq[ast.CommentRange]`（range-over-func，零分配）+ **struct 值**；我们的 `CommentRange` 是 `Ast/CommentRange` 里的 **class**（`AstTypes.cs:98`，Go 是 struct） | **第三批判定：不改**。真实基准语料（TS 6.0.3 的 `src/compiler`，124 文件）上 `Ast.CommentRange` 只有 **0.30 MB**（占全进程分配 0.02%）；且 `CommentRange` 是 `Ast` 公开类型、`Pragma` 还**继承**它，改 struct 必须连带改 `Printer/**`/`Ls/**` 的 `CommentRange?` 取值点 ⇒ 收益 0.02% / 风险大。见 §3.8.1.2 |
| `NameGenerator.PushScope`（2 MB/rep） | Go `pushScope` 同样 `&nameGenerationScope{_next: …}` 每次分配 1–2 个对象 | Go 同款，未动 |
| `EmitPropertyName`/`EmitBindingName`/`ExitNode`/`EmitListItems` 的"自身帧"归因（各 1.5–3% ） | 这些帧是**内联后的归因**（`dotnet-trace` 采样时解析不到内联帧），逐个核对代码后没找到"读却物化"的写法 | 不再追（`EmitListItems` 里原本的 `EmitTrailingCommentsOfPosition` 每次都建 List，已被第 1 项覆盖） |

#### 3.8.1.2 Parse 列表暂存 / 变参 / 声明变换诊断上下文（实测，2026-09-28 第三批）

第三批的三条改动都在"**Go 只有一个分配、C# 却有两个（或 Go 根本没有分配、C# 有）**"这一类，每条都用单变量回退法在同一台仪器上复测过。

**仪器与口径**（沿用第二批，另加两件）：

| 手段 | 为什么用它 |
| --- | --- |
| `artifacts/span-audit` 的 `phases`/`phase <name>`（精确分配字节，305 文件 / 2.72 M 字符 / `-n 5`） | parse 相位的 A/B |
| `artifacts/span-allocreport` 新增 **`ALLOC_KEY=type`**（按"分配类型 + 最内层帧"分组） | `params T[]`、闭包 displayclass、数组这类"**看得见类型、看不出调用点**（ctor 被内联）"的分配，只有这个键能定位到具体方法 |
| **真实基准语料**：`artifacts/bench-tsc6`（TS 6.0.3 的 `src/compiler`，124 文件 / 202,758 行），跑**托管** `src/tsc`（`dotnet src/tsc/bin/Release/net11.0/tsc.dll --project tsconfig.json --extendedDiagnostics`）读 **`Memory allocs`** | 进程级**精确**分配计数器，同一二进制两次跑差 0.015%（1,702,938,672 vs 1,703,193,920）⇒ 比 wall time 可靠得多；配合 `dotnet-trace --profile gc-verbose` + `AllocReport` 做完整调用链归因 |
| AOT `tsc.exe`（`dotnet publish src/tsc -c Release -r win-x64 -p:PublishAot=true`） | 用户的 wall-time 交付表（5 次成对采样取中位） |

**真实编译的调用链归因（`artifacts/span-audit/logs/ts6-before.nettrace`，采样 1668.7 MB ≈ `Memory allocs` 的 96%，
`ALLOC_KEY=type ALLOC_FILTER=TypeScript.Transformers.`）**：

```
98.66 MB  System.Action                       ← DeclarationTransformer.SetupDiagnosticContext
81.95 MB  <>c__DisplayClass102_0               ← DeclarationTransformer.SetupDiagnosticContext
17.37 MB  GetSymbolAccessibilityDiagnostic     ← Declarations.WrapSimpleDiagnosticSelector
10.87 MB  <>c__DisplayClass8_0                 ← Declarations.CreateGetSymbolAccessibilityDiagnosticForNode
10.16 MB  <>c__DisplayClass0_0                 ← Declarations.WrapSimpleDiagnosticSelector
```

即：`setupDiagnosticContext` 那个 `Action` 闭包一个人就占 **180.6 MB**（全进程分配的 **10.8%**），远大于整个 parse 相位。

**已改（每条都有 Go 依据 + A/B 数字）**：

| # | 改动 | Go 依据 | A/B |
| --- | --- | --- | --- |
| 1 | `Ast.NodeKindIs` 的变参 `params Kind[]` → `params ReadOnlySpan<Kind>`（`Ast/Utilities.cs:253`） | `utilities.go:100-102`：`func NodeKindIs(node *Node, kinds ...Kind) bool { return slices.Contains(kinds, node.Kind) }`。调用点传字面量时 Go 把它内联成**栈上数组**（零堆分配）；C# 的 `params Kind[]` 每次都 new 一个数组 | parse **−0.101 B/char**（9.835 → 9.734；trace 里 `Ast.Kind[]` 7.8 MB／776 MB 全部逐条消失） |
| 2 | `Parser.ParseListIndex` / `ParseDelimitedList` 的 `new List<Node>(16)` → `List<Node>? list = null` + 首次 append 才建表，返回 `_nodeSliceArena.Clone(CollectionsMarshal.AsSpan(list))`（null → 默认空 span） | `parser.go:618`、`parser.go:655` 都是 `list := make([]*ast.Node, 0, 16)`（无条件 128 B）；**空列表**（空 block/空 class 体/空 source file…）在 Go 走 `Clone(空) → nil` **零分配** | parse **−0.587 B/char**（9.734 → 9.147） |
| 3 | `Parser.ParseModifiersEx` 同上（`list is not null` ≡ Go 的 `len(list) != 0`） | `parser.go:3921` 同款 `make([]*ast.Node, 0, 16)`；interface/type 成员、形参的绝大多数调用点一个修饰符都没有 | parse **−1.737 B/char**（9.147 → 7.410） |
| 4 | `DeclarationTransformer.SetupDiagnosticContext` 的返回值由 `Action` 闭包改成**值类型** `DiagnosticContextCleanup`（`Transform.Members.cs`），6 个调用点 `cleanupDiagnosticContext()` → `.Restore()` | `transform.go:551-571` 返回 `(bool, func())`：Go 的闭包是**一个** funcval（捕获 tx/oldDiag/oldName/oldWithinObjectLiteralType）；C# 的 `Action` 闭包是**两个**对象（displayclass + 委托）。值类型不逃逸（调用点只把它当 `defer` 用）⇒ 零堆分配 | 该闭包 **180.6 MB → 0**；声明变换根因分配 **226.8 → 44.8 MB**；全进程 `Memory allocs` **1,703.2 → 1,512.7 MB（−11.2%）** |

**净效果**（三条 parse 改动合计，单变量回退法逐条测得，与合计相符）：

| 指标 | before（本批开工前） | after | 变化 |
| --- | --- | --- | --- |
| parse 相位（`phases both 300 0 -n 5`） | 9.835 B/char（127.7 MB） | **7.403 B/char（96.1 MB）** | **−24.7%** |
| 真实编译 TS6 `src/compiler` 的 `Memory allocs` | 1,738.6 MB | **1,512.7 MB** | **−13.0%** |
| scan / bind / print 相位 | 1.945 / 2.812 / 18.183 | 1.945 / 2.812 / 18.234（±1.2% 噪声内） | 0 |

**AOT 交付基准（`artifacts/bench-tsc6`，同机成对采样 5 次取中位）**：

| 指标 | tsgo（原生 Go） | tsc-cs(AOT) | 倍数 | （用户刊出的 tsc-cs "before"） |
| --- | ---: | ---: | ---: | ---: |
| Total | 0.824 s | **1.132 s** | 1.37× | 1.239 s（1.54×） |
| Check | 0.680 s | **0.844 s** | 1.24× | 0.901 s（1.35×） |
| Parse | 0.054 s | **0.154 s** | 2.85× | 0.180 s（3.3×） |
| Memory used | 282.4 MB | **295.8 MB** | 1.05× | 304.5 MB |

> 口径提醒：Parse 的 wall time 在共享机器上重复性只有 ±20%（本次 5 次：0.147–0.223 s），Total ±9%；
> **判读优化以分配字节为准**（parse 相位精确计数器 −24.7%、真实编译 `Memory allocs` −13.0% 都是精确值），
> wall 数字仅作参考。Check 也下降是因为整进程少分配 13% ⇒ GC 压力下降。

**本批测过但判定"不改"的项（如实记录）**：

| 热点 | 依据 | 结论 |
| --- | --- | --- |
| `Scanner.IterateCommentRanges` 的 `CommentRange`（第二批登记项） | 真实基准语料上 `Ast.CommentRange` 只有 **0.30 MB**（0.02%）；第二批的 3.3 MB/rep 来自 305 文件语料（含 `lib.dom.d.ts`），不是本基准的形状 | **不改**（`CommentRange` 是 `Ast` 公开类型 + `Pragma` 继承它，改 struct 要连带改 `Printer/**`/`Ls/**` 的 `CommentRange?` 取值点） |
| 迭代器状态机 `<IterateCommentRanges>d__165`（真实语料 11.48 MB，0.76%） | Go 的 `iter.Seq` 每次调用同样分配一个闭包 | Go 同款，不改 |
| 剩余 44.8 MB 声明变换诊断选择器（`WrapSimpleDiagnosticSelector` 的 displayclass+委托、`CreateGetSymbolAccessibilityDiagnosticForNode` 的内联闭包） | Go 的 `func` 值 = 1 个 funcval；C# "捕获闭包 + 委托字段"必然是 2 个对象 | 归零只能把 `Declarations.GetSymbolAccessibilityDiagnostic` 从**具名委托**改成接口（`Diagnostics.cs` 文件头第 1 条已把"Go `func` 类型 → C# 具名委托"写成约定）—— 为 2.9% 引入结构性偏离 ⇒ **不改** |
| `List<Node>` 的**增长**（真实编译 parse ≈ 44 MB：`AddWithResize`/`set_Capacity`） | 与 Go 的 `append` 翻倍同阶 | 实测把懒表容量从 `List` 默认 4 提到 16 反而 **+0.455 B/char**（7.403 → 7.858，与 Go 的固定 16 容量同口径对比后仍更差）⇒ 保持默认 4 |
| `Core.Splice` 的 `params T[]`、`PagedLinkStore.Get` 的 `?new V?[pageSize]`、`Arena.NewSlice/Clone` 的 `new T[size]` | Go 的 `splice`/`make([]V, pageSize)`/arena 切片同样是独立分配（`arena.go` 文件头第 3 条已记录退化是语言限制） | Go 同款，不改 |

**留给下一个负责人的两项**（都在本任务文件范围外，是真实编译分配榜的第 1、2 名，合计 **24.6%**）：
`Checker.KeyBuilder..ctor` 的 `Byte[]` **197.9 MB**（13.6%，`ALLOC_KEY=type` 归因）与
`Checker.RegisterForUnusedIdentifiersCheck` 的 `Node[]` **173.6 MB**（11.9%）。—— 第 1 项已在 §3.8.1.3 处理，
第 2 项由 fd00fcd 的 `registerForUnusedIdentifiersCheck` 修复覆盖。

#### 3.8.1.3 内置 API 替换：数组拷贝/查找/扫描、变参 → span、`KeyBuilder` 值类型化（实测，2026-09-28 第四批）

**口径与仪器**：两个真实基准 —— TS 6.0.3 `src/compiler`（`artifacts/bench-tsc6/tsconfig.json`，124 文件）与
naxiale（`D:/Source/JavaScript/naxiale/naxiale/tsconfig.json`，`--outDir` 落 `artifacts/`）；**AOT 交付产物**
（`dotnet publish src/tsc -c Release -r win-x64 -p:PublishAot=true`，`System.GC.DynamicAdaptationMode=0` 已在
`Directory.Build.targets` 烘焙、不依赖环境变量），**交错 5 次取中位**；分配字节用 `--extendedDiagnostics` 的
`Memory allocs`（同机同负载逐位级稳定，第四批里 ±0.05%）。脚本与原始输出：`artifacts/_sweep/ab.ps1`
（`aot-baseline` = 改前产物、`aot-new` = 改后产物）。

| 基准 | 指标 | 改前 | 改后 | 变化 |
| --- | --- | ---: | ---: | ---: |
| TS6（124 文件） | **Memory allocs** | 1,306,383,448 / 1,306,500,296 | 1,049,848,992 / 1,051,166,416 | **−19.5…−19.6%** |
| TS6 | wall（AOT，中位） | 1.117 / 1.092 s | 1.028 / 1.033 s | **−5.4…−8.0%** |
| TS6 | Parse / Check / Emit / Total | 0.045 / 0.697 / 0.037 / 0.815 s | 0.048 / 0.646 / 0.037 / 0.772 s | Check −6.4…−7.3%、Total −5.3% |
| TS6 | 诊断条数 | 85 | 85 | 0 差异（逐条一致） |
| naxiale | **Memory allocs** | 478,879,928 / 478,944,648 | 408,720,368 / 408,694,144 | **−14.7%** |
| naxiale | wall（AOT，中位） | 0.522 / 0.443 s | 0.434 / 0.428 s | **−3.4…−16.9%** |
| naxiale | Check / Total | 0.207 / 0.340 s | 0.193 / 0.252 s | −6.3…−6.8% / −25.9% |
| naxiale | 诊断条数 | 0 | 0 | 0 差异 |

（"改前/改后"各列是两个独立的 5 次交错会话，原始输出 `artifacts/_sweep/AB-aot.txt`；`aot-baseline` 产物保留在
`artifacts/_sweep/aot-baseline`，交付产物 `artifacts/bench-tsc6/aot` 已用本批代码重新发布。）

**对 Go 的整体位置**（同机、同两个基准、3 次交错，`artifacts/_sweep/VS-GO.txt`，`tsgo` =
`@typescript/typescript-win32-x64`）：`Total time` 已经**追平** Go（TS6 0.775 s vs 0.767 s，naxiale 0.244 s vs
0.259 s），差距只剩进程固定开销（wall：TS6 1.011 vs 0.839 s、naxiale 0.414 vs 0.318 s）。

##### (1) `Checker.KeyBuilder` 值类型化 —— 本批的绝对大头

Go 的 `keyBuilder` 是**栈上值类型**，`inlineBuffer [192]byte` 内联在结构体里；C# 原来的 `sealed class` 让每个实例
额外分配一个 192 字节的 `byte[]`（`ALLOC_KEY=type` 榜首：**197.9 MB = 13.6%**）+ 对象头本身 ≈ 51 MB。改成
`internal struct KeyBuilder`，192 字节用 C# 12 的 `[InlineArray(192)] struct KeyBuilderInlineBuffer` 内联回去；
所有 14 个构造点都是 `var b = new KeyBuilder();`（栈上）⇒ 分配归零。三处语言限制及其解法（都写在源码注释里）：

* `flow.go` 的 `writeFlowCacheKey` 会改 `b` ⇒ 签名改 `ref KeyBuilder b`（含 4 处递归调用点）；
* 结构体里的**局部函数不能访问 `this`**（CS1673）⇒ `WriteGenericTypeReferences` 的内层闭包提成私有方法
  `WriteGenericTypeReference(ref List<Type>, …, ref bool)`；
* 结构体成员**不能把引用 `this` 字段的 `Span` 返回出去**（CS8170）⇒ 不能用返回 span 的属性，改成各方法内
  `Span<byte> buffer = _inlineBuffer;`（`[InlineArray]` 的隐式 span 转换，零分配）。

单变量 A/B（managed `tsc.dll`，`--project artifacts/bench-tsc6/tsconfig.json`）：
`Memory allocs` **1,336,767,360 → 1,081,403,816（−255.4 MB / −19.1%）**；交错 A/B 的 Check 中位 1.678 → 1.667 s
（不退化）。

##### (2) `params T[]` → `params ReadOnlySpan<T>`（13 个方法）

判据（照 `Ast/Utilities.cs` 的 `NodeKindIs(Node?, params ReadOnlySpan<Kind>)` 先例）：**只对"调用点传内联字面量/
集合表达式"的变参有收益**（编译器在调用点 `stackalloc`，等价于 Go 的 `slices.Contains([]T{…}, x)`）；调用点本来就传
**数组变量**的 ⇒ 零收益（数组→span 只是包装，仍兼容）；**会被存下来的**（`params object?[]` 诊断 args 等）**绝不改**。

| 方法 | 调用点 | 判定 |
| --- | --- | --- |
| `TSPath.Path.CombinePaths` / `ResolvePath` | 159 / 21（几乎都是 1 个附加 path） | 改（模块解析热路径） |
| `Core.Splice` | 16（`types, i, 1` 形态为主） | 改（顺手把 `Array.Copy(items,…)` 换成 `items.CopyTo`） |
| `Core.FirstNonZero<T>` / `FirstNonZero(string?)` | 3 | 改 |
| `Scanner.JSDoc.HasJSDocTag` | 2（在"每个 `@` 调两次"的循环里） | 改（源码里原注释"保持 Go 形状"已按实测改写） |
| `Printer.EmitContext.AddEmitHelper` | 8+（emit 路径） | 改 |
| `Transformers.…ClassFields.Properties.AddPendingExpressions` | 4（全是内联单元素） | 改 |
| `Compiler.IncludeProcessor.AddProcessingDiagnostic` | 4 | 改 |
| `Format.Rules.AnyTokenExcept` / `Format.Rule.Rule(…, RuleFlags)` / `Format.Indent.ShouldIndentChildNode` | 3 / 多处 / 4 | 改（`Kind`/`RuleFlags`/`bool`；不在两个基准窗口内，按"零成本"处理） |
| `Format.Rules.TokenRangeFrom` | 3 | **不改**：方法体把数组**存进** `TokenRange._tokens`，改成 span 只会多一次 `ToArray()` |
| `Collections.NewSetFromItems` / `Transformers…BuildClassMembers` | 15 / 2 | **不改**：调用点全部传已有数组（`Map(...)`/`extraMembers`），零收益 |
| `Json.Json.Marshal/Unmarshal*`（18 个 `params JsonOptions[]`） | 全部零参或已带数组 | **不改**：零参时 Roslyn 发 `Array.Empty<T>()`，无分配可省 |
| `Transformers.Chain.Chain` | 8 | **不改**：`transforms` 被返回的 lambda 捕获，span 不能进闭包 |
| `Compiler.IncludeProcessor` 之外的 `params object?[]`（134 处） | — | **不改**：诊断 args 会存进 `Message.Args`/buildinfo，必须物化 |

实测：managed `Memory allocs` 1,081,403,816 → **1,080,869,904（−0.53 MB / −0.05%）**——恰好落在计数器的
0.5 MB 级噪声底上，即**收益≈0**。原因与上表一致：真正省下来的点是"每个调用点一个 `T[1]`"，而这些点多在
LS / 格式化 / 构建模式，不在两个基准的窗口里。保留理由：改动是**纯减分配**（不存在更差的方向）、语义与 Go 一致
（只有 `HasJSDocTag` 是有意偏离并在注释里写明），且与已有先例同一形状；不保留的理由只有"diff 更大"。

##### (3) 手写循环 → 内置 API（`Array.Copy` / `IndexOf*` / `IndexOfAnyExcept` / `Count` / `SequenceEqual`）

全仓清点（742 个 `.cs`、840 个 `for`、673 个"无嵌套循环体"）：**没有一处是纯 `dst[i] = src[i]` 的同构拷贝**；
绝大多数 `result[i] = f(a[i])` 是 Go 的 `core.Map`/gather（逐元素取值或散列取值，没有 memmove 等价物）；`fill` 只有 1 处
且是带条件的合并逻辑。本批实际改动的 8 处：

| 位置 | Go 语义 | 换成 |
| --- | --- | --- |
| `Core.Version.ComputeVersionMajorMinor` | `strings.IndexFunc` 找第二个 `.` | `string.IndexOf(char)`（ordinal） |
| `Glob.Glob.Split` | 找第一个非 `/` | `ReadOnlySpan<char>.IndexOfAnyExcept('/')` |
| `Ls.JsdocSnippet.IsJSDocSnippetPrefix` / `IsJSDocSnippetSuffix` | "剩下全是 `*`" 判定 | `Span.IndexOfAnyExcept('*') < 0` |
| `Vfs.VfsMatch.ParseSegments` | 数 `*`/`?` 个数 | `Span.Count('*') + Span.Count('?')` |
| `TSPath.Path.GetPathComponentsRelativeTo` | `for j := start; …; result[i] = toComponents[j]` | `Array.Copy(toComponents, start, result, i, …)` |
| `Compiler.FilesParser` | 把 `files` 续到 `libFilesArray` 之后 | `List<SourceFile>.CopyTo(allFiles, libFilesArray.Length)` |
| `Execute.Incremental.Program.SequenceEqual` / `Execute.Watcher.SameStringArray` | `slices.Equal` | `left.AsSpan().SequenceEqual(right.AsSpan())`（与 `Core.SameElements` 同口径） |

实测：**分配字节 0 变化**（这些循环本来就不分配，换的是 memmove/intrinsic 路径），墙钟在两组基准上都低于噪声
（与第 (2) 批一起进 A/B，无法单独归因到 ≤0.5% 的量级）⇒ 保留（无害、更短、与 §3.8 的 span 约定一致）。

##### (4) `Core.Every/Some/Find*` → `Array.TrueForAll/Exists/Find*`：**实测拒绝**（保留手写）

微观基准 `artifacts/_sweep/predbench`（`int[]`/`string[]`/`Box[]`，len=1/4/16，每格 2,000,000 次；
`B/call` = `GC.GetAllocatedBytesForCurrentThread()` 差值 ÷ 次数；原始输出 `artifacts/_sweep/predbench.txt`；
下表取 len=1 的 `int[]`）：

| 形态（len=1） | B/call | ns/call | 备注 |
| --- | ---: | ---: | --- |
| 手写 `foreach` + `Func<T,bool>`，**静态**方法组 | **0** | 3.1–19.2 | 现有 `Core.Some/Every/Find`；Roslyn 把静态方法组缓存进静态字段 |
| 手写 `foreach` + `Func<T,bool>`，**实例**方法组 | **56** | 20.7 | 与"手写 vs 内置"无关：**实例**方法组每次求值都新建委托（见 (6)） |
| `Array.*` + `new Predicate<T>(f)`（选项 b：不动形参） | **56**（实例方法组时 **112**） | 43.6 | 每次调用多一个委托包装 ⇒ **更差** |
| `Array.*` + `Predicate<T>` 形参（选项 a：改形参） | 0 | 19.4 / 20.8 | 无分配但也**无提速**（静态方法组） |

BCL 的 `Array.Exists/TrueForAll/Find/FindLast/FindIndex/FindLastIndex` 是**普通托管循环**（没有向量化/intrinsic——
有 intrinsic 的是 `Array.Copy`/`Array.IndexOf`/`Array.LastIndexOf`/`Array.Fill`/`Array.Clear` 与
`MemoryExtensions.*` 那几个 span 算子），所以"换成内置"既不省时间、选项 (b) 还多一次
委托分配、选项 (a) 要改所有把 `Func<T,bool>` **变量**传进来的调用点 ⇒ **`Core.cs` 的 Some/Every/Find/FindLast/
FindIndex/FindLastIndex 全部保持手写**。`Core.Or`（`params Func<T,bool>[]` 组合器）在 BCL 里没有任何对应物，不动。

##### (5) 本批实测后"不换/不能换"的其它项

| 项 | 依据 | 结论 |
| --- | --- | --- |
| `Json.FloatFormat.FormatExponent` 的 `destination[i] = reverse[count-1-i]` | `count ≤ 4` 的栈上反转；用 `CopyTo + Reverse` 要两趟 | 不改（更长更慢） |
| `Ipc.Timing` 的 `recent[i] = _ring[(_head+i) % _ring.Count]`、`Patience/Lcs`、`SpanMap.maxEnds` 等 | 环形回绕 / 逐元素取**字段**（非连续），没有单次 memmove 等价物 | 不改 |
| `Printer/Checker` 里的 `Map`/`Filter` 手写循环 | Go 的 `core.Map/Filter` 就是循环；`Map` 已是 `new U[n]` + 写满 | 不改（无内置等价物，LINQ 在本仓禁用） |
| `Scanner.SkipTriviaEx` 的逐字符 `switch` | 空白/换行各自带副作用（`StopAfterLineBreak`/`canConsumeStar`），批量跳必须拆开语义 | 不改（风险 > 收益） |

##### (6) 交给下一个负责人（本批实测的新发现，都**没有**在本批改）

| 项 | 量（真实编译） | 说明 |
| --- | ---: | --- |
| `Checker.IsDeprecatedSymbol` 的 `Some(declarations, IsDeprecatedDeclaration)` | `Func<Node,bool>` **20.3 MB**（1.9%） | **实例方法组**每次求值都新建委托（Roslyn 只缓存静态方法组）。把委托提成 `Checker` 字段或写内联循环即可归零；但会动 Checker 的分配/生命周期形状 ⇒ 留给所有者定 |
| `Checker.GetNarrowableTypeForReference` | `Func<Type,bool>` **14.6 MB**（1.4%） | 同上 |
| `Checker.RegisterForUnusedIdentifiersCheck` 的 `Node[]`（第三批登记的 11.9%） | 已不在本批 trace 的榜首 | fd00fcd 已修 |

**门禁（本批）**：`dotnet build TypeScript.slnx`（Debug、Release 各一次）**0 警告 / 0 错误**；
`dotnet test tests/TypeScript.Compiler.Tests -c Release` **4,305 通过 / 0 失败 / 2 跳过**；
harness `--suite compiler --limit 1000 --offset 0`（914 executed）与 `--offset 3000`（951 executed）、
`--suite conformance --limit 1000 --offset 0`（738 executed）全部 `cases with failures = 0` 且 `mismatch = 0`
（基线 3458 / 3374 / 3812 逐字节一致）；额外跑 `--suite fourslash --limit 400` = PASS（`mismatch = 0`），覆盖本批动过的
`Ls/JsdocSnippet`、`Format/**`；两个真实基准的诊断条数逐条不变（TS6 85、naxiale 0）。原始日志：
`artifacts/_sweep/harness-*.txt`。

### 3.9 深度递归必须跑在大栈线程上（实测，M5 结论）

Go 的 goroutine 栈是**可增长**的，TSGO 的 binder/checker/printer 依赖这一点处理深度嵌套的 AST。.NET 的线程栈是**固定大小**，且 `StackOverflowException` 在 .NET Core 上**不可捕获**（进程直接死）。

实测（Binder 切片）：`var x = 1+1+…` 左深嵌套在默认主线程栈上 **2000 项 OK、4000 项进程崩溃**；TSGO 自带的回归用例 `tsc/testdata/tests/cases/compiler/binderBinaryExpressionStress.ts`（6452 个操作数）同样崩；放到 `new Thread(…, maxStackSize: 64 * 1024 * 1024)` 上跑同一个文件 → 通过（symbols=1、bind diagnostics=0）。

**要求**：M6/M7 的 harness、CLI、以及任何"递归遍历 AST 的入口"（bind/check/emit/transform）都必须在**显式指定了大栈的线程**上执行（建议 64–256 MB；`Thread` 的 `maxStackSize` 是构造参数，`Task`/线程池线程不能设置）。TS 的 emitter 自带 trampoline（`binderBinaryExpressionStress.ts` 的注释提到过），printer 可能不需要，但 harness 必须统一处理。

**runtime async 不能替代大栈线程**（实测，`artifacts/stackprobe`，`Features=runtime-async=on` 生效）：

| 写法 | 最大存活深度 | 1,000,000 层耗时 |
| --- | --- | --- |
| 纯同步递归 | 16,000（32,000 崩） | — |
| `async ValueTask<int>` + `await` 一个**已完成**的 `ValueTask` | **8,000（16,000 崩）** | — |
| `async` + 每 64 层 `await Task.Yield()` | ≥1,000,000 | 94 ms |
| `async` + 每层 `await Task.Yield()` | ≥1,000,000 | 211 ms |

结论：
- **`await` 一个已完成的值不会挂起、不会回退栈**（这是 BCL/语言语义，runtime async 只去掉状态机分配，不改变这一点），而且 async 帧比同步帧更贵 → 单纯"写成 async 递归"比同步更早溢出。所以"深度递归 → 改成 runtime async"这个做法本身不成立。
- 只有**真正让出**（`Task.Yield` 或等一个未完成的 `Task`）才会回退栈；代价是续体被调度到线程池 → **跨线程**。而本移植广泛使用 `[ThreadStatic]` 池（`Parser._pooledParser`、`Binder` 的 binder 池、`EmitContext._pooledEmitContext`、Core 的 levenshtein 缓冲）与线程亲和的解析状态，把递归变成"异步 + 让出"会破坏这些假设，需要把它们全部改成 `AsyncLocal`/显式池，收益不抵风险。
- 因此**默认方案 = 大栈线程**（已实测：`binderBinaryExpressionStress.ts` 在 64 MB 栈线程上通过，默认主线程栈上崩溃）。若将来某条路径必须跑在线程池上（例如 LSP 的请求处理），再对该路径单独引入"显式工作栈 trampoline"（TS 的 emitter 就是这么做的），而不是把递归改成 async。

### 3.10 async 与 runtime async（实测，2026‑09‑28）

`artifacts/asyncbench`（参考 hez2010 的 async-benchmarks gist）把各 await 形态的**单次代价**钉死；同源两个工程只差 `Features`（`AsyncBench.csproj` 用 Directory.Build.props 的 `runtime-async=on`，`AsyncBenchNoRa.csproj` 清空 `Features`）。2,000,000 次/项，`dotnet bin/…/<dll>`：

| 场景 | RA **on** ns/op | RA **on** B/op | RA **off** ns/op | RA **off** B/op |
| --- | --- | --- | --- | --- |
| 同步基线 | 1.45 | ~0 | 1.50 | ~0 |
| async 方法、不挂起 | **8.49** | **~0** | 33.34 | **104** |
| `await` 已完成的 `ValueTask` | **2.31** | ~0 | 17.06 | ~0 |
| `await` 已完成的 `Task` | 8.08 | 72 | 18.90 | 72 |
| `await Task.Yield()`（真挂起） | 40.72 | ~0 | 33.56 | ~0 |
| 3 层 async 链 + 叶子处 yield | **127.95** | **240** | 513.28 | 413 |

结论：
- **runtime async 必须保持开启**（`Directory.Build.props` 的 `Features=runtime-async=on`）：不挂起的 async 方法从 33 ns/104 B 降到 8.5 ns/0 B；3 层链从 513 ns/413 B 降到 128 ns/240 B；`await` 已完成的 `ValueTask` 从 17 ns 降到 2.3 ns。M8 的 CLI/host/watch/LSP 会大量用到 async，这就是它的价格表。
- **同步完成的 await 仍然不会回退栈**（2.31 ns 那行就是"什么都不做"的价格）——所以 §3.9 的结论不变：深递归不能靠"写成 async 递归"解决，只能靠大栈线程或显式 trampoline。
- 若将来真要对某条深递归路径上 async trampoline，价格是 **~128 ns/帧 + 240 B/帧**（每 64 层一次 `Task.Yield`，其余为同步完成）——比同步递归（~1.5 ns）贵两个数量级，且会让续体跨线程（`[ThreadStatic]` 池仍安全，只是失去亲和性）。这就是"不首选它"的量化理由。

**把 gist 的每种 async 形态套到"深递归"上**（`artifacts/stackprobe`，默认主线程栈，runtime async ON；栈溢出会杀进程，所以由脚本按深度逐级试）：

| 递归形态 | 最大存活深度 | 存活深度处的 ns/帧 |
| --- | --- | --- |
| 纯同步递归（基线） | 16,000 | 43.5 |
| gist「Async method, no suspension」：每帧 `await` 一个不挂起的 `async Task` | **8,000** | 99.3 |
| gist「Completed ValueTask await」：每帧 `await` 已完成 `ValueTask` | **8,000** | 152.8 |
| gist「state-machine chain」：链式递归、**只在最深处** `await Task.Yield()` | **4,000** | 759.6 |
| 每 64 层 `await Task.Yield()`（trampoline） | **≥512,000**（未触顶） | 89.8 |

两点明确结论：
1. **"每帧 await（哪怕已完成）"会让可递归深度减半、每帧更贵**；"只在叶子 yield" 最差（整条链都压在栈上，且每帧多了 async 帧）——所以 gist 的 async 用法**不能**用来解决深递归。
2. 只有**周期性真正让出**（每 64 层 `Task.Yield()`）才把深度上界抬到几十万级，代价 ~90 ns/帧。这印证 §3.9 的默认方案（大栈线程），并给出"若某条路径必须留在池线程上"时的备选实现与价格。

### 3.11 Server GC 的 DATAS：Parse 相位 3× 的元凶（实测，2026‑09‑28）

**现象**：AOT 产物（`artifacts/bench-tsc6/aot/tsc.exe`，`Directory.Build.targets` 已开 ServerGC，.NET 9+ 的
Server GC **默认启用 DATAS**）在真实基准语料（TS 6.0.3 `src/compiler` 77 文件 + lib 链 + node_modules = **124
文件 / 202,758 行 / 9,843,912 字符**）上是 `Parse 0.189 s / Check 0.941 s / Total 1.303 s`（5 次交错中位），
而 Go 原生 tsgo 是 `0.057 / 0.678 / 0.820` ⇒ Parse **3.3×**。**这 3.3× 不是代码质量，是 GC 配置**：
把 DATAS 关掉（`System.GC.DynamicAdaptationMode=0`，堆数仍是 24）后我们变成
`Parse 0.048 s / Check 0.746 s / Total 0.887 s` ⇒ Parse **比 tsgo 还快（0.84×）**，Total 1.08×。

**为什么 DATAS 在这里帮倒忙**（官方文档 + 实测一致）：

- DATAS 按**存活集（long-lived data）**给 gen0 预算，`f(size_MB) = (20 - conserveMemory)/sqrt(size_MB)`；
  而经典 Server GC 按**吞吐**给预算。我们的编译进程存活集只有几十 MB、单个 Parse 相位却分配 117 MB
  （12.47 B/char，精确计数器）⇒ gen0 预算被压到很小。
- DATAS **总是从 1 个堆起步**，要靠"几次 GC"才长到核数；而 Parse 相位正好是进程最开头的那 117 MB。
- 文档原文：*"With DATAS enabled, the number of Gen0 and Gen1 GCs is significantly higher."*、
  *"When a process starts, DATAS always starts with one heap… allocating threads might experience waits… it takes
  a few GCs before the number of heaps gets increased."*（<https://learn.microsoft.com/dotnet/standard/garbage-collection/datas>）
- 实测完全吻合：gen0 **3.7/rep + gen1 1.7/rep**（DATAS 开）vs **0.2 / 0**（关），
  `GC.GetTotalPauseDuration()` 15.5–28.9 ms/rep vs 0.2–9.2 ms/rep；并行 parse 时暂停占 wall **54.5% vs 10.9%**，
  且**并行度被杀到 ≈1.0**（Parse：并行 0.189 s vs `--singleThreaded` 0.184 s），关掉 DATAS 后并行度恢复到
  2.8×（0.141 → 0.051 s，tsgo 是 0.135 → 0.053 s，2.5×）。
- **DATAS 没有换到内存**：`Memory used` 302.9 MB（开）vs 302.9 MB（关），Go 是 289.1 MB。

**测量矩阵**（AOT，全部交错、中位数；原始日志 `artifacts/parse-profile/logs/matrix*.txt`）：

| 配置 | Parse | Bind | Check | Emit | Total | Memory used |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo（原生 Go） | 0.057 | 0.021 | 0.678 | 0.063 | 0.820 | 289,136K |
| 默认：ServerGC + DATAS（24 堆） | 0.189 | 0.040 | 0.941 | 0.051 | 1.303 | 302,863K |
| **`DynamicAdaptationMode=0`（堆数 24）** | **0.048** | 0.020 | 0.746 | 0.039 | 0.887 | 302,867K |
| `GCHeapCount=20` | 0.047 | 0.020 | 0.774 | 0.041 | 0.901 | 302,886K |
| `GCHeapCount=16` | 0.048 | 0.021 | 0.787 | 0.041 | 0.919 | 302,872K |
| `GCHeapCount=12` | 0.047 | 0.020 | 0.767 | 0.041 | 0.894 | 302,891K |
| `GCHeapCount=8` | 0.069 | 0.057 | 0.827 | 0.037 | 1.008 | 302,994K |
| `GCHeapCount=4` | 0.086 | 0.025 | 0.858 | 0.036 | 1.046 | 302,924K |
| `gcServer=0`（工作站 GC） | 0.245 | 0.142 | 1.351 | 0.066 | 1.823 | 319,749K |

要点：**设 `GCHeapCount` 同时也关掉 DATAS**（文档：设置 HeapCount 会使 DATAS 失效），所以 12/16/20 三档
快只是"DATAS 被关"的副作用，不是堆数本身；堆数降到 8/4 反而变慢 ⇒ **不要顺手固定堆数**。
核数可移植性（`DOTNET_PROCESSOR_COUNT` 模拟 4/8/12/24 核）：DATAS 关掉在各档都更快
（Parse 2.0×/2.1×/3.4×/4.3×），且不需要任何"与核数相关的公式" ⇒ **推荐式与核数无关**。

**落地（AOT 与 JIT 都是"构建时烘焙"，AOT 没有 runtimeconfig.json 可改）**：

```xml
<!-- src/tsc/tsc.csproj（或 Directory.Build.targets 的 Exe 分支）——SDK 自带属性名，
     写进 JIT 的 runtimeconfig.json，并在 NativeAOT 下由 ilc 烘焙进镜像 -->
<GarbageCollectionAdaptationMode>0</GarbageCollectionAdaptationMode>
```

若只想给单个项目加、或要给 CLR 不认的键（`GCHeapCount` 没有 SDK 属性）用显式 item：

```xml
<ItemGroup>
  <RuntimeHostConfigurationOption Include="System.GC.DynamicAdaptationMode" Value="0" Trim="false" />
  <!-- 只在实测证明某核数上更优时才加：<RuntimeHostConfigurationOption Include="System.GC.HeapCount" Value="12" Trim="false" /> -->
</ItemGroup>
```

**验证方法（已实测，二进制级）**：`dotnet publish src/tsc -c Release -r win-x64 -p:PublishAot=true
-p:GarbageCollectionAdaptationMode=0 -o <out>`，**不设任何环境变量**跑：
`Parse 0.048 s / Total 0.864 s`（对照现产物 0.155 / 1.029），再把环境变量设回 `DOTNET_GCDynamicAdaptationMode=1`
应能复现 `0.167 / 1.063`（env 覆盖构建值）⇒ 说明烘焙生效。JIT 侧同样有效（`dotnet tsc.dll`：Parse 0.255 →
0.152 s，−40%）。

**适用条件 / 风险**：CLI 是短命进程、存活集小、分配率高 —— DATAS 的目标（按存活集缩堆）正好拿不到收益却付
吞吐代价，实测内存还完全不变，故对 `src/tsc` 与内置宿主（`TypeScript.Compiler` 被嵌入时）建议**关闭**；
长命的 LSP/watch 进程若在内存受限环境跑，可先测 TCP（`% pause time in GC`）再决定 —— DATAS 的设计目标是
内存，不是吞吐。**库消费者（把 `TypeScript.Compiler` 嵌进自己进程）不受我们产物配置影响**，宿主若同样是
"短命 + 高分配率"请自行设 `System.GC.DynamicAdaptationMode=0`。
仪器与全部原始数字：`artifacts/parse-profile/NOTES.md`。

### 3.12 Check 相位 1.4× 的两个真因（实测，2026‑09‑28）

**现象**：naxiale（331 文件 / 153,020 行，含 lib + `@types/node` + `@types/vscode`，未开 `skipLibCheck`）
上 Check 是 `tsgo 0.193–0.211 s vs 我们 0.280–0.299 s`（4 checker，1.45×）；`--checkers 1` 是 0.314–0.320 vs
0.413–0.430（1.33×）。加 `--skipLibCheck` 后两边都是 ~0.008 s ⇒ 差距全在 **lib/`.d.ts` 检查**。
诊断工具：**Go 的 `--generateTrace` 两侧产物格式逐字节一致**（`trace.json` + `types_0.json`）⇒ 可以拿
`types_0.json` 做"类型级差分"（每条记录 = 一次 `newType`，含 id/symbolName/firstDeclaration/flags/display）。
二分语料（`artifacts/check-gap/probes/*.json`，`--checkers 1` 确定性）后定位到 **两个互不相干的真因**：

**真因 A（正确性 bug）：`core.Same` 被当成 `slices.Equal` 用**。Go 的 `core.Same` 比较"同一底层数组"
（`&s1[0] == &s2[0]`），`slices.Equal` 是**逐元素**比较；本仓的 `Core.Same` 如实映射前者，但
`resolveObjectTypeMembers`（`checker.go:19452`）里的 `slices.Equal(typeParameters, typeArguments)`
被写成了 `Same(...)`。对**未实例化的泛型类型**（`typeArguments` 是它自己的 `typeParameters`，但是另一个
数组实例）Go 判定相等、走"直接用 declaredMembers"分支，我们判定不等 ⇒ **对整张 declared members 表做了一次
恒等实例化**：凭空造出数百个 transient symbol、数百次类型实例化，类型计数却不变（所以 `Types` 一直是准的）。
同一类错误还有两处（`relater.go:2383/3385` 的模板字面量 `texts` 比较，比较 `[]string` 内容），已一并改成
`Core.SameElements`（新增，语义 = `slices.Equal`，逐元素 + nil≡空）。

| 指标（naxiale，`--checkers 1`） | tsgo | 修前 | 修后 |
| --- | ---: | ---: | ---: |
| Symbols | 155,019 | 157,544（+2,525） | **155,018（−1）** |
| Instantiations | 83,059 | 84,311（+1,252） | **83,050（−9）** |
| Types | 72,700 | 72,752（+52） | 72,752（+52，见"版本漂移"） |
| Check 时间 | 0.312 | 0.428 | 0.347 |

**真因 B（性能 bug，O(n²)）：`registerForUnusedIdentifiersCheck` 的 append 被写成整表拷贝**。
Go 是 `append(links.identifierCheckNodes, node)`（摊销 O(1)），移植成了 `[.. arr, node]` —— **每注册一个声明
就全量拷贝一次**，对 `lib.dom.d.ts`/`lib.es5.d.ts` 这种单文件注册上千个声明的输入是 O(n²)。
`AllocReport` 抓 Check 相位分配调用链的头名就是它：**198.7 MB = 全部分配的 32.1%**（naxiale 全量 trace，
618.4 MB 采样）。改成 `List<Node>` + `Add` 后同一 trace 采样降到 **418.9 MB（−32%）**。

**修后结果**（AOT 产物，交错 7 次取中位；原始日志 `artifacts/check-gap/logs/*`，脚本
`artifacts/check-gap/bench.ps1`）：

| 语料 / 配置 | 指标 | tsgo | 修前 | 修后 |
| --- | --- | ---: | ---: | ---: |
| naxiale，默认 checker | Check | 0.199 | 0.284 | **0.198** |
| naxiale，默认 checker | Total | 0.253 | 0.342 | **0.258** |
| naxiale，`--checkers 4` | Check / Total | 0.201 / 0.253 | 0.284 / 0.342 | **0.202 / 0.265** |
| naxiale，`--checkers 1` | Check | 0.312–0.314 | 0.428 | **0.346–0.347** |
| naxiale，`--checkers 1` | Total | 0.365–0.368 | 0.483 | **0.402–0.405** |
| lib-only（48 文件 / 75,393 行），`--checkers 1` | Check | 0.139 | 0.205 | **0.164** |

⇒ Check 从 **1.40× → 1.00×**（4 checker / 默认）/ **1.37× → 1.10×**（`--checkers 1`）；Total 从 1.35× → **1.02×**
（默认）。`--checkers 4` 下我们的 Check（0.202）与 tsgo（0.203）打平。

**多 checker 并行度（OS 口径 `TotalProcessorTime / wall`，naxiale，中位 3）**：

| 配置 | wall | cpu | 并行度 | parse | bind | check | emit | total |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo 默认 | 0.313 | 0.906 | 2.81 | 0.033 | 0.009 | 0.199 | 0.010 | 0.253 |
| 我们 默认 | 0.474 | 2.406 | **5.80** | 0.025 | 0.007 | 0.198 | 0.015 | 0.258 |
| tsgo `--checkers 4` | 0.313 | 1.359 | 4.36 | 0.033 | 0.010 | 0.201 | 0.008 | 0.253 |
| 我们 `--checkers 4` | 0.443 | 1.922 | 4.34 | 0.027 | 0.007 | 0.202 | 0.013 | 0.265 |
| tsgo `--checkers 1` | 0.425 | 1.047 | 2.24 | 0.032 | 0.010 | 0.314 | 0.010 | 0.368 |
| 我们 `--checkers 1` | 0.566 | 1.859 | 3.17 | 0.028 | 0.007 | 0.346 | 0.009 | 0.402 |

要点：`--checkers 1→4` 我们的 Check 是 0.346 → 0.202（**1.71×**），tsgo 是 0.314 → 0.201（1.56×）
⇒ **任务切分/扩展性不比 Go 差**；剩下的 `wall` 差（0.474 vs 0.313）几乎全在**相位之外**
（0.474−0.258=0.216 s vs tsgo 0.313−0.253=0.060 s：进程启动 + config + 模块解析/文件 IO + 收尾），
而我们每 wall 秒烧的 CPU 是 tsgo 的 **2.4×**（Server GC 24 堆 + 更高的分配率），这是 Total 之外的独立课题。

**剩余差距的性质（"本质"清单）**：

1. **版本漂移（不可消除，除非放弃移植上游 HEAD）**：`Types` 恒定多 52 个（lib-only 多 16 个）——它们是
   上游 commit `bb209ac1c0`"Distinguish between non-distributed and distributed type parameters (#64237)"
   （2026‑09‑16）带来的 **distributed type parameter**（每个分配式条件类型别名多一个类型参数对象）。
   基准二进制是 npm 上的 **typescript 7.0.2**，早于该 commit；本仓按上游 HEAD（2026‑09‑25）移植。
   把该特性临时关掉后类型计数**逐个精确相等**（lib-only 1522=1522、DOM 语料 28,257=28,257），
   而 `Symbols`/`Instantiations` 只差 −7/−0（对应 npm lib 比我们的 bundled lib 多 11 个 identifier/2 行的
   既有偏差）⇒ 除散布式类型参数外，类型/符号/实例化**已经与 7.0.2 完全对齐**。
2. **多 checker 下的 CPU 口径**：4 checker 时 `cpu/wall` 我们是 4.5、tsgo 3.3（同一 wall 我们烧更多 CPU：
   Server GC 24 堆 + 更高分配率），wall 上 Check 已打平；`--checkers 1` 仍留 1.11×（残余在关系/实例化缓存
   的常数因子，需更细的采样仪器才能继续下钻）。
3. **分配量仍是 tsgo 的数十倍**（`Memory allocs` 478.9 MB vs Go 的 926,180 次 malloc，口径不可直接比）：
   `AllocReport` 显示修后头部是 `KeyBuilder..ctor` 47 MB（Go 的 `keyBuilder` 是 192 字节 **栈上结构体**，
   我们是含 `byte[192]` 的类）、`Dictionary.Resize` 45 MB、`LinkStore` 的 `new V()`（`CreateInstanceOfT`）
   15 MB。这些是**已知且已文档化的移植偏离**（`Core/LinkStore.cs` 文件头 1、`Checker.C5.CacheKeys.cs`），
   继续优化属于"分配率"而非"算法"，收益需单独度量。

## 4. 生成代码

TSGO 里 `*_generated.go` 的输入是语言中立的（`tools/scripts/tsc/ast.json`、`internal/stringutil/_scripts` 的 Unicode 数据、`diagnostics/diagnosticMessages.json`…）。C# 侧由 `tools/TypeScript.CodeGen` 消费同一份输入生成 `*.Generated.cs`：

- **Unicode 表**（`identifier_parts_generated.go`、`js_case_generated.go`）：生成器读 TSGO 已提交的生成文件（本机没有 npm 依赖，且这样能保证与 TSGO 出货行为逐位一致），输出等价的 range table + `Rune` 查找。生成器幂等，重跑不产生 diff。
- 生成文件头写清生成器与输入，**不允许手工编辑**。
- 未生成的 Go 文件一律手工移植，保持与 Go 逐行对应，不要"顺便重构"。

## 5. 测试

- 单元测试：`tests/TypeScript.Compiler.Tests`，xunit，按包分目录，1:1 复刻 Go 的 `*_test.go`。Go 的表驱动测试用 `[Theory]`/`[MemberData]`。
- 集成测试（后续）：`tests/TypeScript.Harness` 跑 TSGO `tsc/testdata`（`tests/cases/**` + `baselines/reference/**`）。TSGO 仓库根目录由环境变量 `TSGO_ROOT` 指定，默认探测 `D:\Source\GitHub\TypeScript-Go`。
- 差分验证：本机有全局 `tsc`（TSGO 的 CLI，7.0.2）与 `node`（v24），可用它们产生期望输出交叉验证：JSON 格式用 `tsc --showConfig`/`--generateTrace` 取证（`docs/json.md` §1，`artifacts/json-oracle`），JS 数字语义用 Node/V8 交叉验证（`JsNum` 的测试里已有 10 条真实 Node 对照）。
- 本机没有 Go 工具链，也没有 `D:\Source\GitHub\TypeScript-Go\node_modules`：任何"必须跑 Go 才能得到"的期望值，改为从 TSGO 已提交的 baseline / 测试数据里取（或用独立实现重算，如 `artifacts/gen-verify` 用 JS 重写 Go 的 `unicode.Is` 来校验生成表）。
- **基准对拍口径（2026-09-28 实测；完整证据见 `artifacts/lines-probe/FINDING.md`）**：与全局 `tsc`（npm `typescript@7.0.2` = TS tag `v7.0.2`）对拍 `--extendedDiagnostics` 时，有三处**已知且非移植缺陷**的差异，比较前必须先对齐口径：
  1. **lib 数据版本偏差 ⇒ `Lines` 差 2**：我们 bundle 的是 TSGO **main** 的 libs（与 TSGO 检出 `tsc/internal/bundled/libs` 108/108 逐字节一致），发布版 7.0.2 的 libs 旧一版 —— `lib.es2018.intl.d.ts:50`、`lib.es2020.intl.d.ts:468` 各**多 1 行**（`Intl.PluralRulesConstructor` 的可调用签名），上游 PR **#63608**（2026-07-27）已删掉这两行。受控实验（**同一个** `tsc.exe`，只换 lib 目录）：go+npm libs `Lines 202760`，go+**我们的** libs `Lines 202758` = 我们的输出，且 77 个语料文件逐文件逐字节一致（192,636 行）⇒ **行起始算法忠实**。做基准应**优先与 main 分支构建的 tsgo 对比**，或显式注明 lib 版本；`Lines`/lib 内容类差异不是移植缺陷。
  2. **`Symbols`/`Types`/`Instantiations` 随 checker 池大小变化**：这三个计数器是"各 checker 内部创建次数之和"，池大小（默认 4）不同则分流不同、数字不可比。实测 4 checker 时我们低 2.8%/3.6%/4.8%，**`--checkers 1` 时两边差 ≤0.05%** ⇒ 跨实现/跨版本比较这些计数器必须固定 `--checkers 1`。
  3. **`Identifiers` 差约 1,709（我方多）**：计数点在解析期（`Parser.newIdentifier`），两边计数点/call site 1:1，小文件与全部单构造探针相等；只在大文件上按该文件标识符数的 0.35%–2.3% 多算（`src/compiler/checker.ts` 单文件 +721）。已验证**可观测无差**：85 条诊断逐字节一致 + `--declaration` 产物 `diff -r` **0 差异** ⇒ 属"被丢弃/未进树的标识符节点计数"（推测解析、恢复路径或未缓存 JSDoc 之类），**不影响行为**；具体构造类别尚未定位（JSDoc 各种位置、34 个常见表达式/类型构造、推测解析回滚语义均已排除）。

## 6. 里程碑

| 里程碑 | 内容 | 状态 |
| --- | --- | --- |
| M0 | 工程骨架：slnx、编译器库、测试工程、CodeGen 工具；移植规范定稿 | done |
| M1a | `Debug`、`StringUtil`（含 Unicode 生成表 + CodeGen 生成器）、`TSPath` | done（200 测试通过；表数据与 Go 源对拍一致） |
| M1b | `Collections`（含 OrderedMap 的 JSON 钩子）、`Json`、`JsNum` | done（JsNum 的 JS 数字语义与真 Node/V8 交叉验证） |
| M2 | `Core`（`Text`、枚举、`CompilerOptions`、`Arena`、`Semaphore`、`Workgroup`、`Bfs`、`LinkStore`…） | done |
| M3 | `Ast`（由 `ast.json` 生成 + 手写工具函数）、`Diagnostics`（由 `diagnosticMessages.json` 生成）、`Locale` | done（Ast 生成物+核心+`utilities.go`+`SourceFile`+`precedence`/`deepclone`/`diagnostic`；`Diagnostics` 2213 条消息与 Go 生成物逐字段全等；13 个语言的本地化数据按 TSGO 产物嵌入。缺口：`deepclone_test.go` 需 parser 才能跑；`PositionMap` 在 C# 里是恒等映射（§3.1），非 ASCII 用例按 C# 契约改写） |
| M4 | `Scanner`、`Parser` → 能 parse 出 AST，跑 parser 基线 | done（scanner：与真 tsc 对拍 27 例的（诊断码, UTF-16 位置）0 不匹配 + 16 万 token 不变量审计；parser：6 个分片按 Go 行范围移植、语料测试 17,423 unit / 111,860 语句 / 2,585,355 节点 0 违反、与 tsc 差分 130 例 0 不一致、baseline 反方向审计 92 条缺失全部归因到 M5/惰性 JSDoc。基线对比本身属 M6 的 harness） |
| M5 | `Binder`、`Checker` → 语义错误基线 | 前置全部就位（binder + tsoptions + module/packagejson/semver/modulespecifiers/symlinks + vfs 家族 + tracing/evaluator/pseudochecker/nodebuilder/astnav/sourcemap + contentmapper/ipc/jsonrpc + 自写 xxh3）；**checker（60,847 行）未开始**，切分计划见 docs/checker-plan.md |
| M6 | `Printer` + transformers → emit 基线（`tests/cases/compiler` 通过） | printer（15,161 行）done（printer_test 的 553+15+8 行表用例与 Go 逐字节一致）；**transformers（24,404 行）+ compiler（6,695 行）待做** |
| M7 | `Compiler`/`Program` → `tsc` 可编译真实工程 | 待做（含 harness，契约见 docs/harness.md；注意深递归必须跑在大栈线程上，§3.9） |
| M8 | `execute`、`watch`、`project`、`ls`/`lsp` | 待做（`tsoptions` 已在 M5 前置中完成） |
