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
