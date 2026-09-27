# Baseline harness 契约（在 C# 里重跑 TSGO 的 compiler/conformance 测试）

本文件是"在 C# 里复刻 TSGO 测试 harness"的事实清单，全部来自读 TSGO 源码（`D:\Source\GitHub\TypeScript-Go`），
行号是取证时的位置。实现落在 `tests/TypeScript.Harness`（尚未创建）。**验收标准**：`tsc/testdata/baselines/reference/**` 是唯一 golden，逐字节比较。

## 1. 入口与用例来源

- Go 侧入口：`internal/testrunner/compiler_runner_test.go` 的 `TestLocal` → `runCompilerTests`，起两个 runner：
  `NewCompilerBaselineRunner(TestTypeRegression)`（suite `compiler`）与 `(TestTypeConformance)`（suite `conformance`）
  （compiler_runner.go:43-64），分别扫 `testdata/tests/cases/compiler`、`testdata/tests/cases/conformance`（递归，`\.tsx?$`）。
  另有 `TestTranspile`（`tests/cases/transpile`，`\.[cm]?[tj]sx?$`）。
- baseline 路径：`baselines/{local,reference}/<suite>/<用例 basename><扩展名>`；**扁平按 basename** ⇒ 两个 suite 的 basename 不能重名
  （compiler_runner_test.go:23-31 就是断言这一点）。
- 子测试名 = 用例相对路径（`TestLocal/yieldStringLiteral.ts`）；单用例过滤：`go -C ./tsc test -run='TestLocal/<name>' ./internal/testrunner`。
- fourslash / tsctests / tsoptions / project / lsp 是**另外的套件**，各自有不同的 baseline 根目录，不在本文件范围。
- 前提：`bundled.Embedded` 为真（`--noembed` 会整包 skip）。

## 2. 用例格式（`internal/testrunner/test_case_parser.go`）

- 指令正则 `(?m)^\/\/\s*@(\w+)\s*:\s*([^\r\n]*)`（:41）：`//` 必须在**第 0 列**，选项名 lower 化，值 TrimSpace，
  允许结尾 `;`（:281-289 再 TrimSuffix）。
- `// @filename: x.ts` 分隔多文件 unit（:138-279）：第一个 `@filename` 之前的非注释内容会 panic；没有 `@filename` 时整份内容
  是唯一 unit，**unit 名 = 用例文件 basename**。unit 内容按 `\r?\n` 切分后用 `\n` 重新 join ⇒ **内存里一律 LF**。
- `@currentdirectory` 决定 currentDirectory，默认 `/.src`（compiler_runner.go:34）。
- 多文件分组规则（compiler_runner.go:283-330）：默认全部 unit 一起编译；若最后一个 unit 含 `require(` 或 `/// <reference path`
  或用例带 `@noImplicitReferences`，则只把最后一个 unit 交给编译器，其余进 otherFiles。

## 3. 选项（`internal/testutil/harnessutil/*`）

- 分类（SetOptionsFromTestConfig，harnessutil.go:291-316）：`typescriptversion` 忽略；命中 `compilerOptions`
  （= `tsoptions.OptionsDeclarations` + allowNonTsExtensions/noErrorTruncation/suppressOutputPathCheck/noCheck）→ 编译器选项；
  命中 harness-only（harnessutil.go:340-396）→ harness 选项；**其它 → 直接 fail**（"Unknown compiler option"）。
- harness-only：useCaseSensitiveFileNames、baselineFile、includeBuiltFile、fileName、libFiles、noImplicitReferences、
  currentDirectory、symlink、link、noTypesAndSymbols、fullEmitPaths、reportDiagnostics、captureSuggestions。
- 值解析按声明 Kind（:442-490）：Boolean **只接受小写 true/false**；Enum 走 `enummaps.go` 的字面量表；
  List/ListOrElement 按逗号切；IsFilePath 的按 cwd 绝对化。
- **逗号的双重语义**：选项在 varyBy 集合里时逗号 = 多配置（varyBy 由声明推导：`!IsCommandLineOnly && Kind∈{Boolean,Enum}
  && 任一 Affects*`，再加 noEmit/isolatedModules，compiler_runner.go:150-176）；否则（List 类，如 `@lib`/`@types`）只是列表。
  变体名 = 排序后的 `k=lowercase(v)` 逗号串（harnessutil.go:1026-1036），支持 `*`（枚举全量）与 `-x`/`!x`（排除），上限 25。
- 用例级强制默认（**先于**用例选项设置，可被用例覆盖，harnessutil.go:95-105）：`NewLine`=CRLF、
  `SkipDefaultLibCheck`=true、`NoErrorTruncation`=true、`UseCaseSensitiveFileNames`=true、`CurrentDirectory`=`/.src`。
- TSGO 语义默认（用例没写时）：target 默认 `ScriptTargetLatestStandard`（=ES2025）、module 由 target 推（⇒ES2022）、
  moduleResolution 默认 Bundler、`strict` **不物化**而走 `GetStrictOptionValue`（`@strict: false` 与不写等价）。
- 被移除的选项会产生 option 诊断（baseUrl/outFile/target=es5/module=AMD|System|UMD/Classic|node10/alwaysStrict=false/…），
  但 harness 的处置是 **skip 该变体**（`SkipUnsupportedCompilerOptions`，harnessutil.go:1236-1263），AMD 与 outFile 直接 fatal；
  另有 41 个按文件名的 skip 列表（compiler_runner.go:78-124）与 8 个只跳过 emit 的名字（:429-437）。这些 skip 规则必须照搬，
  否则会去实现 ES5 这种本编译器已移除的路径。

## 4. 输出文件与字节级规则

- 每个用例生成哪些 baseline 由选项决定（`.js`、`.d.ts`、`.symbols`、`.types`、`.errors.txt`、`.js.map`、`.sourcemap.txt`、
  `.trace.json`、`.contentmapper`）；`@noTypesAndSymbols` 关掉后两者；`@noEmit` 不产 `.js`。
- `.js` baseline：每个 section 前是 `//// [<file>] ////` 头，多文件用例先列输入、再列产物；`@fullEmitPaths` 时用相对路径。
- **行尾是混合的**：harness 生成的分隔行一律 CRLF，而"从源码照抄的行"保留源文本自己的 LF（实测 `baselines/reference/compiler/yieldStringLiteral.js`）。
  `.errors.txt` 全部 CRLF 且**没有结尾换行**（末尾是 `\r\n    `）。`.symbols`/`.types` 全 CRLF。baseline 不写 BOM。
- 无内容哨兵 `NoContent = "<no content>"`：写本地 `<name>.delete` 且**不报错**（baseline.go:59-76）；"reference 不存在 + actual 是
  NoContent" 也是静默通过（这就是 `@noEmit` 用例没有 `.js` 的原因）。
- 必须复刻的生成期规范化（tsbaseline/util.go:23-49 等）：
  1. 路径前缀去除表：`/.ts/`、`/.lib/`、`/.src/`、`bundled:///libs/` 删掉；`file:///./ts/` 等换成 `file:///`（含保留尾分隔符的变体）。
  2. `lib*.d.ts(l,c)` → `(--,--)`；related 位置 → `lib*.d.ts:--:--`。
  3. 诊断排序：`compiler.SortAndDeduplicateDiagnostics`，error baseline 再按 `compareDiagnostics` 排一次。
  4. 产物顺序：先按 program 输入文件顺序配对，未配对的按 unit 名排序。
  5. trace 去随机化：版本号 → `FakeTSVersion`；缓存态文本规范化（harnessutil.go:552-604）。
  6. source map baseline 末尾追加 sokra 可视化 base64 链接。
  7. `.js` baseline 里带 noCheck 对比段（再跑一次 `{noCheck:true}` 并插 patience diff）。
- **比较与接受**：逐字节相等，**没有比较期规范化**；失败只报"文件变了"，不输出 diff。`hereby baseline-accept` 是
  "local/** 无条件覆盖 reference/**（排除 `.delete` 标记，`.delete` 对应的 reference 文件被删除）"。

## 5. 实现顺序建议

1. runner 骨架：枚举/解析用例、子测试名、变体名、baseline 路径映射。
2. `.errors.txt` → 3. `.js`（含 d.ts 与 noCheck 段）→ 4. `.symbols`/`.types` → 5. `.js.map`/`.sourcemap.txt` → 6. trace/contentmapper。
7. 每加一类扩展名，都用同一个用例与 Go 侧产物对拍（文件名集合必须完全一致）。

## 6. 不能用全局 `tsc` 代替 harness

全局 `tsc`（TSGO 7.0.2）不认 `// @option` 指令，不产 `//// [file]` 头 / `.symbols` / `.types` / `FakeTSVersion` / 路径规范化，
且对 ES5、AMD/UMD/System、node10/classic 会报"Option has been removed" —— 它只能当**参考实现**用来对拍单点行为（例如本仓库
`docs/json.md` 里用 `--showConfig`/`--generateTrace` 钉 JSON 格式），不能当 harness。
