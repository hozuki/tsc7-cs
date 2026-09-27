# M5/M6 移植计划：checker 与 emit 链

本文件是给"下一波 agent/下一次会话"用的**分片边界**，避免重复调研。命名/类型/命名规则一律见 `docs/architecture.md`、`docs/ast.md`。

## 1. 依赖顺序（已确认）

```
M5 前置（进行中）  binder / tsoptions / module+packagejson / modulespecifiers / tracing+evaluator+pseudochecker+nodebuilder+astnav+sourcemap / printer
        ↓
checker（60,847 行，见 §2）
        ↓
transformers（24,404 行，41 文件）+ compiler（6,695 行，14 文件：program/emitter/host/checkerpool…）
        ↓
harness（tests/TypeScript.Harness：testdata → baselines/reference 逐字节比较，契约见 docs/harness.md）
```

checker 的 import 面（决定它要等哪些包）：`ast, binder, collections, core, debug, diagnostics, evaluator, jsnum, module, modulespecifiers, nodebuilder, parser, printer, pseudochecker, scanner, stringutil, tracing, tsoptions, tspath, astnav`。

## 2. checker 的分片（按 Go 文件名 / 行窗口；`partial class Checker`）

单个 `checker/checker.go` 就有 **32,581 行 / 1,509 个顶层声明**，按行窗口切成 8 片（边界都落在顶层声明起始行上）：

| 片 | Go 行范围 | 行数 | 声明数 | 内容（首/末声明） |
| --- | --- | --- | --- | --- |
| C1 | 1–4057 | 4057 | 195 | `CheckMode`…`getIndexTypeOrString`（状态/缓存键/键类型/Checker 结构体/链接缓存/基础类型操作） |
| C2 | 4058–8145 | 4088 | 170 | `checkForOfStatement`…`isInConstructorArgumentInitializer`（语句检查、控制流相关检查） |
| C3 | 8146–12220 | 4075 | 173 | `checkTemplateExpression`…`getThisParameterFromNodeContext`（表达式/上下文类型/JSX 相关） |
| C4 | 12221–16321 | 4101 | 182 | `getContextualThisParameterType`…`getResolvedMembersOrExportsOfSymbol`（签名/参数/成员解析） |
| C5 | 16322–20338 | 4017 | 226 | `lateBindMember`…`isLateBindableAST`（迟到绑定/符号合并/属性解析） |
| C6 | 20339–24427 | 4089 | 208 | `getReturnTypeOfSignature`…`computeConstantEnumMemberValue`（返回类型/枚举/常量求值） |
| C7 | 24428–28490 | 4063 | 247 | `evaluateEntity`…`getNormalizedTupleType`（求值/元组/索引访问） |
| C8 | 28491–32582 | 4092 | 193 | `getSingleBaseForNonAugmentingSubtype`…`GetAliasedSymbol`（继承/别名/公共查询 API） |

其余 checker 文件（独立文件，各自一个分片）：

| 文件 | 行数 | 备注 |
| --- | --- | --- |
| `types.go` | 1,496 | **Type/Signature/IndexInfo 等数据模型**（checker 的地基，先做） |
| `links.go` | 50 | `nodeLinkStore`/`symbolArenaLinkStore`（用 Core 的 `PagedLinkStore`，注意页粒度 `Has`） |
| `utilities.go` | 1,910 | 检查器内部工具（与 ast/utilities.go 不同） |
| `relater.go` | 5,046 | 关系判定（isTypeAssignableTo/identity/comparable…）；与 types.go 耦合最紧 |
| `flow.go` | 2,761 | 控制流分析（flow 图、narrowing） |
| `grammarchecks.go` | 2,237 | 语法/语义文法检查（与 errors baseline 直接相关） |
| `inference.go` | 1,685 | 类型推断 |
| `jsx.go` | 1,488 | JSX 检查；`jsdoc.go` 100 |
| `nodebuilderimpl.go` 3,714 / `nodebuilder.go` 302 / `nodebuilder_hover.go` 605 / `nodebuilderscopes.go` 251 / `pseudotypenodebuilder.go` 765 | 5,637 | checker 的节点构造器（给 LS/emit 用） |
| `emitresolver.go` 1,323 / `services.go` 1,151 / `exports.go` 400 | 2,874 | 对外服务（emit 解析、语言服务） |
| `nodecopy.go` 900 / `symbolaccessibility.go` 876 / `mapper.go` 330 / `symboltracker.go` 129 / `tracer.go` 366 / `printer.go` 356 / `stringer_generated.go` 25 | 2,982 | 复制/可见性/映射/追踪/打印 |

## 3. 建议的推进顺序（每步都要能编译 + 测试绿）

1. **B1 地基**：`types.go` + `links.go` + `utilities.go` + `checker.go` C1。这一步决定 `Checker` 的字段/缓存/Type 模型，必须先做，其他人按它的形状写。
2. **B2**：`checker.go` C2–C5（4 个分片）。
3. **B3**：`checker.go` C6–C8 + `flow.go` + `inference.go`（5 个分片）。
4. **B4**：`relater.go` + `grammarchecks.go` + `jsx.go`/`jsdoc.go` + nodebuilder 家族 + services/exports/emitresolver + nodecopy/symbolaccessibility/mapper/symboltracker/tracer/printer（≈ 17k 行，6–7 个分片）。
5. **M6**：`transformers`（41 文件 24,404 行，按 `transformers/{estransforms,inliners,jsxtransforms,moduletransforms,tstransforms,declarations}` 分片）+ `compiler`。
6. **M7**：`execute`（tsc CLI/watch/build）+ harness（`docs/harness.md`）。

### 3.1 M6/M7 的已知前置清单（已勘查，避免重复调研）
| 项 | 规模 | 做法 |
| --- | --- | --- |
| `internal/bundled` | 108 个 `lib.*.d.ts`（3.8 MB）+ `bundled.go`/`libs_generated.go` | 按 Diagnostics 的本地化数据那样**作为 `EmbeddedResource` 嵌入**（生成器产出"lib 名 → 资源名"的表 + `LoadLib(name)`），不要生成 3.8MB 的 C# 字符串字面量 |
| `internal/testutil/harnessutil` | 1,682 行（依赖 `compiler`/`bundled`/`execute/incremental`） | harness 的前置，等 M6/M7 落地后再移植 |
| `internal/testutil/tsbaseline` + `baseline` | 1,403 + 188 | 同上；契约与字节级规则见 `docs/harness.md` §4 |
| `internal/testrunner/test_case_parser.go` | 299 行（依赖 `harnessutil`/`tsoptions`/`tsoptionstest`） | `tsoptions` 落地后即可先做（`@filename` 拆分 + 选项解析），不必等 `harnessutil` |
| `tests/TypeScript.Harness` | 新工程 | 跑 `TSGO_ROOT/tsc/testdata`：`tests/cases/**` → `baselines/{local,reference}`，逐字节比较；跑法见 `docs/harness.md` §1、§5 |

## 4. 验收（M5）

- 编译通过 + 既有测试全绿是底线；**语义**正确性最终由 M6/M7 的 harness 用 TSGO 的 `.errors.txt` baseline 判定（见 `docs/harness.md` §4）。
- 中间可用"与真 `tsc` 对拍诊断（码 + UTF-16 位置）"做差分（scanner/parser 已经这么做过，脚本在 `artifacts/scanner-diff` 与 `artifacts/parser-oracle`；checker 阶段可扩展同一套：`tsc --noEmit` 的输出作为 oracle）。
- checker 自带 `checker_test.go`（看它是什么形状，能搬就搬）。

## 5. C# 形状契约（**所有 checker 分片动手前必读**）

### 5.1 Type 模型：三件套合并成一个类层次
Go 的 `types.go` 把「数据」和「断言」分成三层：`Type` 结构体（`flags/objectFlags/id/symbol/alias/checker/data`）、
`TypeData` 接口（7 个 `As*()`）、`TypeBase` 结构体（`struct{ Type }`，7 个 `AsX()` 默认返回 nil）。
C# 里**合并**：

- `abstract class Type`：字段照抄 Go 的 `Type`（Go 未导出字段 → `_camelCase`，跨 partial/跨类访问用 `internal`：`internal TypeFlags _flags; internal ObjectFlags _objectFlags; internal TypeId _id; internal Symbol? _symbol; internal TypeAlias? _alias; internal Checker _checker;`）；**`data TypeData` 不移植**。
- `TypeData`、`TypeBase` 两个名字在 C# 里**不出现**；具体类型（`IntrinsicType`/`LiteralType`/`TupleType`/`StructuredType`/`ObjectType`/`UnionType`/…）直接**继承 `Type`**（Go 的 `TypeBase` 嵌入 → C# 基类）。
- `AsX()`：在 `Type` 上 `internal virtual X? AsX() => null;`，Go 里 override 的那些在 C# 里 `override` 返回 `this`（与 Go 的「TypeBase 返回 nil、具体类型返回 t」逐一对齐）。
- 改写：`t.data.(*X)` → `(X)t`；`x, ok := t.data.(*X)` → `t as X` + null 判断；`TypeData` 型的参数/字段/返回 → `Type`；`.AsType()` → 该对象本身（`AsType` 不移植）。
- 与 AST 的 `nodeData` 折叠同源（docs/ast.md §2）。`TypeData` 在 Go 里只出现 11 次（types.go 7 / checker.go 4），合并是纯机械的。

### 5.2 所有权（谁定义什么；别人只读）
| 东西 | 归属 |
| --- | --- |
| `Checker` 结构体的**全部字段**（checker.go:586 起） | **C1 独有**。其它片只读，不改其定义；缺字段 → 报告 |
| `NodeLinks`/`SymbolLinks`/`ValueSymbolLinks`/`MappedSymbolLinks`/`Signature`/`IndexInfo`/`TypeFlags`/`ObjectFlags`/`TypeId`/`TypeAlias`/… | `types.go`（B1a） |
| `nodeLinkStore[V]`/`symbolArenaLinkStore[V]` | `links.go`（B1b）→ C# 命名 `NodeLinkStore<V>`/`SymbolLinkStore<V>`（§2「未导出类型按 C# 习惯命名，不必同名」） |
| 包级函数（Go 无接收者） | **同一个非 static `public sealed partial class Checker`** 里的 `internal static`/`private static` 成员（先例：`Parser`/`Binder`/`Printer`；`public static partial class Checker` 与实例类会 CS0708）。调用点可 `using static TypeScript.Checker.Checker;`（只导入静态成员） |
| `partial class Checker` 的实例方法 | 各片各写自己那段；**一个方法只能有一处定义** |

Go 里同时存在 `func (c *Checker) foo()` 与包级 `func foo()` → C# 方法名冲突，包级那个加 `Worker` 后缀（§2 先例，如 `EmitSourceFileWorker`）。

### 5.3 每片的编译现实（**重要**）
checker 是紧耦合单包：C1–C8 互相调用，还依赖 `relater`/`flow`/`grammarchecks`/`jsx`/`inference`/`nodebuilderimpl`/…，
**单文件不可能编译通过**。所以每片的验收是：

1. **机械核对**：你负责的 Go 行/文件范围内的**每个顶层声明**都要在 C# 里有对应成员（成员名 = Go 名按 §2 规则；struct→class、方法→PascalCase 方法、常量→PascalCase 成员/枚举成员）。0 未解释缺失，报告里给出核对表（Go 声明 → C# 成员）。
2. **语法自检**：`dotnet build src/TypeScript.Compiler/TypeScript.Compiler.csproj`，**你负责的文件里不得有 CS1xxx 语法错误**。`CS0103/CS0117/CS0246/CS1061/CS1501/CS1503`（符号来自别的切片/尚未落地）**是预期噪声，不要去"修"**——也不要去建别人该建的符号。
3. **报告里必须给出**：①你的文件里期望缺失的**跨片符号清单**（名字 + 出处 Go 文件:行）；②机械核对表；③你做的所有 `Go→C#` 语义改写（每条一行理由）。

### 5.4 机械改写与已知坑（照做，不要自创）
- `(T, bool)` → `bool TryX(..., out T)`；`(T, error)` → 抛异常；`panic`/`Debug.Fail` → `Debug.Fail`；Go 用 `reflect` 浅拷贝 → `MemberwiseClone()`；`iota` → enum（数值与 Go 一致、成员名 = Go 常量名去类型前缀）。
- **任何 .cs 禁止 `using System.Diagnostics;`**（`Debug.Fail` 会绑到 BCL → FailFast 杀进程）；要 `Stopwatch` 用别名。
- 字符串/位置一律 UTF-16 代码单元（AST 的 `Text`/span API）；string 零值 `""` 字段必须显式初始化。
- **调用 C# 侧已有 API 前先 grep 确认真实签名**（`Ast.Node.Kind`、`NodeFactory.*`、`Ast.GetNodeId`、`Core.PagedLinkStore.Get(ulong)`…），不要按 Go 猜。
- 已知坑：命名空间与类同名（`TypeScript.Checker` 命名空间 + `Checker` 类 → 类内引用同包类型直接写裸名，**不要**写 `Checker.Foo`，会解析到命名空间）；`Dictionary` 索引器读缺失键抛异常（Go 的 map 读返回零值）→ 用 `TryGetValue`/`GetValueOrDefault`；**方法组转委托**：签名匹配时 `Some(xs, IsFoo)` 直接传是**合法且更省**的（只分配一个委托；lambda 还要多一个闭包对象），只有在重载歧义/泛型推断失败（CS1503/CS0121，常见于 `?:`、`Map<T,U>` 推断不出 U）时才现场换成 `x => Foo(x)`；语句内的 `goto`/`fallthrough`/`break loop` 要逐处对齐（Go 的 `break loop` → C# `goto` 或提取局部函数）。
- 只改你负责的文件。**不要**碰 `.editorconfig`/csproj/docs/别人的文件；跨包缺符号一律"报告"，不要就地发明。

### 5.5 命名裁决（2026-09-28，实测先例；checker 各片必须统一）
| Go 侧 | C# 侧 | 仓库先例（实测） |
| --- | --- | --- |
| **未导出**字段（`c.unknownSymbol`、`Type.flags`、`c.mu`） | `_camelCase`；本类内用 `private`，跨 partial 文件/跨类用 `internal _camelCase` | `Binder`：`internal Node? _node;`、`private FlowNode? _unreachableFlow;`；`Printer`：`private SourceFile? _currentSourceFile;` |
| **导出**字段（`ResolvedModule.Path`） | `internal`/`public` + PascalCase **原名、不加前缀**（属于要镜像的公开 API） | `Module/Resolver.cs`：`internal string Path = "";` |
| 未导出字段的访问器 `func (x *X) Name() T` | `internal T Name() => _name;` | 同上 |
| Go 包级函数 | 同一个 `public sealed partial class Checker` 里的 `internal static`/`private static` 成员（**禁止** `public static partial class Checker` → CS0708） | `Parser`/`Binder`/`Printer` |
| `func (c *Checker) foo()` 与包级 `func foo()` 同名 | 包级那个加 `Worker` 后缀 | `EmitSourceFileWorker` |
| `func(source, target *Type) Ternary`（Go 无名函数类型） | `internal delegate Ternary TypeRelatedComparer(Type source, Type target);`（**relater.go 所有**，`checker.go` 的 `compareProperties` 用它） | 新增 |
| `relater.go:87` 的 `ErrorReporter` | `internal delegate void ErrorReporter(Message message, params object?[] args);`（**relater.go 所有**，逐字对应 `args ...any`；热路径传 `null`） | 新增 |
| `TypeComparer`（types.go:1447） | 归 `types.go` 片 | 新增 |

→ 一句话：**`_camelCase` 只针对 Go 未导出字段；Go 导出名一律原样 PascalCase、不加前缀。**

### 5.6 命名遮蔽（C# 同命名空间优先于 using，会**静默**绑错类型）
C# 的名字解析是「当前命名空间 → using 引入的命名空间」，所以 `TypeScript.Checker` 里声明的类型会**盖住** `using TypeScript.Collections;` 等引入的同名类型——不报错，直接绑错，行为悄悄变。

- 已踩：`utilities.go:879` 的未导出 `orderedSet[T]`（Add **不去重**，只有 `contains` 判重）若命名成 `OrderedSet<T>`，会盖住 `collections.OrderedSet`（`Add` **会去重**）→ `Types.cs` 的 `_deferredNodes` 语义会变。**裁决：改名 `SmallOrderedSet<T>`**（未导出类型按 C# 习惯命名，§2）。
- 包级审计结果（`TypeScript.Checker` 140 个类型 vs 其它包）：`Host`（vs `ContentMapper.Host`）、`ParseFlags`（vs `Parser.ParseFlags`）、`Tracer`（vs `Module.Tracer`）——这三个是 Go **导出**名，按 API 对等必须保留；**引用另一包的那个时写全限定名**（`TypeScript.Module.Tracer`）。
- 新增任何 C# 侧自造类型名前，先 `grep -rn "class <名字>\b" src/TypeScript.Compiler --include=*.cs` 确认不撞。

## 7. M6 切片（transformers 24,404 行 + compiler 6,695 行；已按文件勘查）

命名空间：`internal/transformers` → `TypeScript.Transformers`，`internal/transformers/estransforms` → `TypeScript.Transformers.EsTransforms`（其余子包同理），`internal/compiler` → `TypeScript.Compiler`（**注意**：`TypeScript.Compiler` 已是程序集名，命名空间仍按 §2 用同名，无冲突）。

| 片 | 文件（行数） | ≈行数 |
| --- | --- | --- |
| T1 基础设施 | `transformers/{chain.go,transformer.go,modifiervisitor.go,utilities.go,destructuring.go}`（62/41/28/375/510）+ `transformers/inliners/*`（102） | ~1,100 |
| T2 class fields | `estransforms/classfields.go`（3,618） | 3,618 |
| T3 ES 装饰器 | `estransforms/esdecorator.go`（2,751） | 2,751 |
| T4 async 族 | `estransforms/{async.go,forawait.go,using.go}`（984/856/799） | 2,639 |
| T5 ES 其余 | `estransforms/{objectrestspread.go,namedevaluation.go,utilities.go,optionalchain.go,taggedtemplate.go,logicalassignment.go,exponentiation.go,usestrict.go,nullishcoalescing.go,definitions.go,optionalcatch.go,classthis.go}`（593/535/289/240/175/113/90/50/49/43/37/28） | 2,242 |
| T6 声明发射 | `declarations/*`（transform.go 2,995 / diagnostics.go 736 / tracker.go 252 / 其余 2 文件） | 4,223 |
| T7 TS 转译 | `tstransforms/*`（legacydecorators 1,038 / runtimesyntax 995 / typeserializer 511 / typeeraser 393 / metadata 390 / 其余 2 文件） | 3,509 |
| T8 模块变换 | `moduletransforms/*`（commonjsmodule 2,162 / externalmoduleinfo 390 / esmodule 372 / 其余 2 文件） | 3,095 |
| T9 JSX | `jsxtransforms/jsx.go`（1,209） | 1,209 |
| T10 程序 | `compiler/program.go`（2,468）+ `projectreferencedtsfakinghost.go`（227） | 2,695 |
| T11 文件加载 | `compiler/{fileloader.go,filesparser.go,fileInclude.go,includeprocessor.go,projectreferencefilemapper.go,projectreferenceparser.go}`（1,114/599/325/184/192/115） | 2,529 |
| T12 发射与主机 | `compiler/{emitter.go,checkerpool.go,emitHost.go,host.go,processingDiagnostic.go,pkg.go}`（576/493/143/122/135/2） | 1,471 |

依赖：transformers 依赖 `ast`/`core`/`printer`/`factory`(NodeFactory)/`jsnum`/`module`/`tsoptions`/`tspath`/`harness`(无)…；`compiler` 依赖 `bundled`（lib.*.d.ts）、`checker`、`module`、`vfs`、`tsoptions`、`tracing`、`execute`(**仅 tsc/watch 子包**，core program 不需要)。

已知前置：`internal/bundled`（108 个 lib，EmbeddedResource）——已在进行中；`internal/testutil/*` 与 `internal/execute` 属 M7。
