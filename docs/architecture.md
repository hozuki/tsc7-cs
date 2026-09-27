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
