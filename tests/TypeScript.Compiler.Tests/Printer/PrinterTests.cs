// ported from: internal/printer/printer_test.go
//
// 移植映射（docs/architecture.md §5）：
//   * Go 的 `[]struct{…}` 表驱动 + t.Run(name, …) → xunit 的 [Theory] + [MemberData]（同 DiagnosticsTests.cs）；
//     每条用例各自并行（对应 Go 的 t.Parallel()），用例名写进 ITestOutputHelper 输出。
//   * Go 的 `gotest.tools/assert` → xunit 的 `Assert.Equal(expected, actual)`；Go 的 `t.Fatalf` → 同样用
//     Assert.Equal（失败信息由 xunit 给出）。
//   * Go 的 `parsetestutil.*` / `emittestutil.*` → 本目录的 ParseTestUtil / EmitTestUtil（各自的文件头有偏离说明）。
//
// C# 侧的命名处理（都是可用性问题，不影响用例内容）：
//   * `TypeScript.Printer` 既是命名空间，又包含同名类 `Printer`；在 `namespace TypeScript.Tests` 里裸写
//     `Printer` 会先绑到外层命名空间的 `TypeScript.Printer`（CS0118，与 Diagnostic.cs 文件头记的
//     Json/Locale 同一个坑），所以 `Printer` 类用别名 `PrinterClass`，`printer.NewEmitContext()` →
//     `PrinterClass.NewEmitContext()`（Go 的包级函数落在 C# 的 `partial class Printer` 上）。
//   * Go 的 `ast.NodeFactory` → 别名 `AstNodeFactory = TypeScript.Ast.NodeFactory`：printer 包将来也有自己的
//     `NodeFactory`（printer/factory.go），两个 `using` 同时可见时裸写 `NodeFactory` 会歧义（CS0104）。
//   * Go 的 `ast.NewNodeFactory(ast.NodeFactoryHooks{})` → `NewNodeFactory(new NodeFactoryHooks())`
//     （`using static TypeScript.Ast.Ast;`，与 Go 的包级函数同名同参）。
//   * Go 每个合成节点测试开头的 5 行样板（`var factory ast.NodeFactory` /
//     `factory.NewSourceFile(... "/file.ts" ..., factory.NewToken(ast.KindEndOfFile))` / `MarkSyntheticRecursive`）
//     抽成 `NewSyntheticFile(factory, statements)`；每个用例的工厂调用序列与期望输出仍与 Go 逐行对应。
//
// 未移植的用例（原因见每条的注释）：
//   * `TestPartiallyEmittedExpression`（printer_test.go:2509）：需要 `internal/transformers` +
//     `internal/transformers/tstransforms`（`NewTypeEraserTransformer`），这两个包尚未移植。
using TypeScript.Ast;
using TypeScript.Core;
using Xunit;
using Xunit.Abstractions;
using static TypeScript.Ast.Ast;
using AstNodeFactory = TypeScript.Ast.NodeFactory;
using PrintHandlers = TypeScript.Printer.PrintHandlers;
using PrinterClass = TypeScript.Printer.Printer;
using PrinterOptions = TypeScript.Printer.PrinterOptions;
using Path = TypeScript.TSPath.Path;

namespace TypeScript.Tests;

public class PrinterTests(ITestOutputHelper output)
{
    // Go: TestEmit 的表记录（title/input/output/jsx）。
    public sealed record EmitCase(string Title, string Input, string Output, bool Jsx = false);

    public static TheoryData<EmitCase> EmitTests => new()
    {
        new("StringLiteral#1", ";\"test\"", ";\n\"test\";"),
        new("StringLiteral#2", ";'test'", ";\n'test';"),
        new("NumericLiteral#1", "0", "0;"),
        new("NumericLiteral#2", "10_000", "10000;"),
        new("BigIntLiteral#1", "0n", "0n;"),
        new("BigIntLiteral#2", "10_000n", "10000n;"), // TODO: Preserve numeric literal separators after Strada migration
        new("BooleanLiteral#1", "true", "true;"),
        new("BooleanLiteral#2", "false", "false;"),
        new("NoSubstitutionTemplateLiteral", "``", "``;"),
        new("NoSubstitutionTemplateLiteral#2", "`\n`", "`\n`;"),
        new("RegularExpressionLiteral#1", "/a/", "/a/;"),
        new("RegularExpressionLiteral#2", "/a/g", "/a/g;"),
        new("NullLiteral", "null", "null;"),
        new("ThisExpression", "this", "this;"),
        new("SuperExpression", "super()", "super();"),
        new("ImportExpression", "import()", "import();"),
        new("PropertyAccess#1", "a.b", "a.b;"),
        new("PropertyAccess#2", "a.#b", "a.#b;"),
        new("PropertyAccess#3", "a?.b", "a?.b;"),
        new("PropertyAccess#4", "a?.b.c", "a?.b.c;"),
        new("PropertyAccess#5", "1..b", "1..b;"),
        new("PropertyAccess#6", "1.0.b", "1.0.b;"),
        new("PropertyAccess#7", "0x1.b", "0x1.b;"),
        new("PropertyAccess#8", "0b1.b", "0b1.b;"),
        new("PropertyAccess#9", "0o1.b", "0o1.b;"),
        new("PropertyAccess#10", "10e1.b", "10e1.b;"),
        new("PropertyAccess#11", "10E1.b", "10E1.b;"),
        new("PropertyAccess#12", "a.b?.c", "a.b?.c;"),
        new("PropertyAccess#13", "a\n.b", "a\n    .b;"),
        new("PropertyAccess#14", "a.\nb", "a.\n    b;"),
        new("ElementAccess#1", "a[b]", "a[b];"),
        new("ElementAccess#2", "a?.[b]", "a?.[b];"),
        new("ElementAccess#3", "a?.[b].c", "a?.[b].c;"),
        new("CallExpression#1", "a()", "a();"),
        new("CallExpression#2", "a<T>()", "a<T>();"),
        new("CallExpression#3", "a(b)", "a(b);"),
        new("CallExpression#4", "a<T>(b)", "a<T>(b);"),
        new("CallExpression#5", "a(b).c", "a(b).c;"),
        new("CallExpression#6", "a<T>(b).c", "a<T>(b).c;"),
        new("CallExpression#7", "a?.(b)", "a?.(b);"),
        new("CallExpression#8", "a?.<T>(b)", "a?.<T>(b);"),
        new("CallExpression#9", "a?.(b).c", "a?.(b).c;"),
        new("CallExpression#10", "a?.<T>(b).c", "a?.<T>(b).c;"),
        new("CallExpression#11", "a<T, U>()", "a<T, U>();"),
        // {title: "CallExpression#12", input: `a<T,>()`, output: `a<T,>();`}, // TODO: preserve trailing comma after Strada migration
        new("CallExpression#13", "a?.b()", "a?.b();"),
        new("NewExpression#1", "new a", "new a;"),
        new("NewExpression#2", "new a.b", "new a.b;"),
        new("NewExpression#3", "new a()", "new a();"),
        new("NewExpression#4", "new a.b()", "new a.b();"),
        new("NewExpression#5", "new a<T>()", "new a<T>();"),
        new("NewExpression#6", "new a.b<T>()", "new a.b<T>();"),
        new("NewExpression#7", "new a(b)", "new a(b);"),
        new("NewExpression#8", "new a.b(c)", "new a.b(c);"),
        new("NewExpression#9", "new a<T>(b)", "new a<T>(b);"),
        new("NewExpression#10", "new a.b<T>(c)", "new a.b<T>(c);"),
        new("NewExpression#11", "new a(b).c", "new a(b).c;"),
        new("NewExpression#12", "new a<T>(b).c", "new a<T>(b).c;"),
        new("TaggedTemplateExpression#1", "tag``", "tag ``;"),
        new("TaggedTemplateExpression#2", "tag<T>``", "tag<T> ``;"),
        new("TypeAssertionExpression#1", "<T>a", "<T>a;"),
        new("FunctionExpression#1", "(function(){})", "(function () { });"),
        new("FunctionExpression#2", "(function f(){})", "(function f() { });"),
        new("FunctionExpression#3", "(function*f(){})", "(function* f() { });"),
        new("FunctionExpression#4", "(async function f(){})", "(async function f() { });"),
        new("FunctionExpression#5", "(async function*f(){})", "(async function* f() { });"),
        new("FunctionExpression#6", "(function<T>(){})", "(function <T>() { });"),
        new("FunctionExpression#7", "(function(a){})", "(function (a) { });"),
        new("FunctionExpression#8", "(function():T{})", "(function (): T { });"),
        new("ArrowFunction#1", "a=>{}", "a => { };"),
        new("ArrowFunction#2", "()=>{}", "() => { };"),
        new("ArrowFunction#3", "(a)=>{}", "(a) => { };"),
        new("ArrowFunction#4", "<T>(a)=>{}", "<T>(a) => { };"),
        new("ArrowFunction#5", "async a=>{}", "async (a) => { };"),
        new("ArrowFunction#6", "async()=>{}", "async () => { };"),
        new("ArrowFunction#7", "async<T>()=>{}", "async <T>() => { };"),
        new("ArrowFunction#8", "():T=>{}", "(): T => { };"),
        new("ArrowFunction#9", "()=>a", "() => a;"),
        new("DeleteExpression", "delete a", "delete a;"),
        new("TypeOfExpression", "typeof a", "typeof a;"),
        new("VoidExpression", "void a", "void a;"),
        new("AwaitExpression", "await a", "await a;"),
        new("PrefixUnaryExpression#1", "+a", "+a;"),
        new("PrefixUnaryExpression#2", "++a", "++a;"),
        new("PrefixUnaryExpression#3", "+ +a", "+ +a;"),
        new("PrefixUnaryExpression#4", "+ ++a", "+ ++a;"),
        new("PrefixUnaryExpression#5", "-a", "-a;"),
        new("PrefixUnaryExpression#6", "--a", "--a;"),
        new("PrefixUnaryExpression#7", "- -a", "- -a;"),
        new("PrefixUnaryExpression#8", "- --a", "- --a;"),
        new("PrefixUnaryExpression#9", "+-a", "+-a;"),
        new("PrefixUnaryExpression#10", "+--a", "+--a;"),
        new("PrefixUnaryExpression#11", "-+a", "-+a;"),
        new("PrefixUnaryExpression#12", "-++a", "-++a;"),
        new("PrefixUnaryExpression#13", "~a", "~a;"),
        new("PrefixUnaryExpression#14", "!a", "!a;"),
        new("PostfixUnaryExpression#1", "a++", "a++;"),
        new("PostfixUnaryExpression#2", "a--", "a--;"),
        new("BinaryExpression#1", "a,b", "a, b;"),
        new("BinaryExpression#2", "a+b", "a + b;"),
        new("BinaryExpression#3", "a**b", "a ** b;"),
        new("BinaryExpression#4", "a instanceof b", "a instanceof b;"),
        new("BinaryExpression#5", "a in b", "a in b;"),
        new("BinaryExpression#6", "a\n&& b", "a\n    && b;"),
        new("BinaryExpression#7", "a &&\nb", "a &&\n    b;"),
        new("ConditionalExpression#1", "a?b:c", "a ? b : c;"),
        new("ConditionalExpression#2", "a\n?b:c", "a\n    ? b : c;"),
        new("ConditionalExpression#3", "a?\nb:c", "a ?\n    b : c;"),
        new("ConditionalExpression#4", "a?b\n:c", "a ? b\n    : c;"),
        new("ConditionalExpression#5", "a?b:\nc", "a ? b :\n    c;"),
        new("TemplateExpression#1", "`a${b}c`", "`a${b}c`;"),
        new("TemplateExpression#2", "`a${b}c${d}e`", "`a${b}c${d}e`;"),
        new("YieldExpression#1", "(function*() { yield })", "(function* () { yield; });"),
        new("YieldExpression#2", "(function*() { yield a })", "(function* () { yield a; });"),
        new("YieldExpression#3", "(function*() { yield*a })", "(function* () { yield* a; });"),
        new("SpreadElement", "[...a]", "[...a];"),
        new("ClassExpression#1", "(class {})", "(class {\n});"),
        new("ClassExpression#2", "(class a {})", "(class a {\n});"),
        new("ClassExpression#3", "(class<T>{})", "(class<T> {\n});"),
        new("ClassExpression#4", "(class a<T>{})", "(class a<T> {\n});"),
        new("ClassExpression#5", "(class extends b {})", "(class extends b {\n});"),
        new("ClassExpression#6", "(class a extends b {})", "(class a extends b {\n});"),
        new("ClassExpression#7", "(class implements b {})", "(class implements b {\n});"),
        new("ClassExpression#8", "(class a implements b {})", "(class a implements b {\n});"),
        new("ClassExpression#9", "(class implements b, c {})", "(class implements b, c {\n});"),
        new("ClassExpression#10", "(class a implements b, c {})", "(class a implements b, c {\n});"),
        new("ClassExpression#11", "(class extends b implements c, d {})", "(class extends b implements c, d {\n});"),
        new("ClassExpression#12", "(class a extends b implements c, d {})", "(class a extends b implements c, d {\n});"),
        new("ClassExpression#13", "(@a class {})", "(\n@a\nclass {\n});"),
        new("OmittedExpression", "[,]", "[,];"),
        new("ExpressionWithTypeArguments", "a<T>", "a<T>;"),
        new("AsExpression", "a as T", "a as T;"),
        new("SatisfiesExpression", "a satisfies T", "a satisfies T;"),
        new("NonNullExpression", "a!", "a!;"),
        new("MetaProperty#1", "new.target", "new.target;"),
        new("MetaProperty#2", "import.meta", "import.meta;"),
        new("ArrayLiteralExpression#1", "[]", "[];"),
        new("ArrayLiteralExpression#2", "[a]", "[a];"),
        new("ArrayLiteralExpression#3", "[a,]", "[a,];"),
        new("ArrayLiteralExpression#4", "[,a]", "[, a];"),
        new("ArrayLiteralExpression#5", "[...a]", "[...a];"),
        new("ArrayLiteralExpression#6", "const array = [/* comment */];", "const array = [ /* comment */];"),
        new("ObjectLiteralExpression#1", "({})", "({});"),
        new("ObjectLiteralExpression#2", "({a,})", "({ a, });"),
        new("ShorthandPropertyAssignment", "({a})", "({ a });"),
        new("PropertyAssignment", "({a:b})", "({ a: b });"),
        new("SpreadAssignment", "({...a})", "({ ...a });"),
        new("Block", "{}", "{ }"),
        new("VariableStatement#1", "var a", "var a;"),
        new("VariableStatement#2", "let a", "let a;"),
        new("VariableStatement#3", "const a = b", "const a = b;"),
        new("VariableStatement#4", "using a = b", "using a = b;"),
        new("VariableStatement#5", "await using a = b", "await using a = b;"),
        new("EmptyStatement", ";", ";"),
        new("IfStatement#1", "if(a);", "if (a)\n    ;"),
        new("IfStatement#2", "if(a);else;", "if (a)\n    ;\nelse\n    ;"),
        new("IfStatement#3", "if(a);else{}", "if (a)\n    ;\nelse { }"),
        new("IfStatement#4", "if(a);else if(b);", "if (a)\n    ;\nelse if (b)\n    ;"),
        new("IfStatement#5", "if(a);else if(b) {}", "if (a)\n    ;\nelse if (b) { }"),
        new("IfStatement#6", "if(a) {}", "if (a) { }"),
        new("IfStatement#7", "if(a) {} else;", "if (a) { }\nelse\n    ;"),
        new("IfStatement#8", "if(a) {} else {}", "if (a) { }\nelse { }"),
        new("IfStatement#9", "if(a) {} else if(b);", "if (a) { }\nelse if (b)\n    ;"),
        new("IfStatement#10", "if(a) {} else if(b){}", "if (a) { }\nelse if (b) { }"),
        new("DoStatement#1", "do;while(a);", "do\n    ;\nwhile (a);"),
        new("DoStatement#2", "do {} while(a);", "do { } while (a);"),
        new("WhileStatement#1", "while(a);", "while (a)\n    ;"),
        new("WhileStatement#2", "while(a) {}", "while (a) { }"),
        new("ForStatement#1", "for(;;);", "for (;;)\n    ;"),
        new("ForStatement#2", "for(a;;);", "for (a;;)\n    ;"),
        new("ForStatement#3", "for(var a;;);", "for (var a;;)\n    ;"),
        new("ForStatement#4", "for(;a;);", "for (; a;)\n    ;"),
        new("ForStatement#5", "for(;;a);", "for (;; a)\n    ;"),
        new("ForStatement#6", "for(;;){}", "for (;;) { }"),
        new("ForInStatement#1", "for(a in b);", "for (a in b)\n    ;"),
        new("ForInStatement#2", "for(var a in b);", "for (var a in b)\n    ;"),
        new("ForInStatement#3", "for(a in b){}", "for (a in b) { }"),
        new("ForOfStatement#1", "for(a of b);", "for (a of b)\n    ;"),
        new("ForOfStatement#2", "for(var a of b);", "for (var a of b)\n    ;"),
        new("ForOfStatement#3", "for(a of b){}", "for (a of b) { }"),
        new("ForOfStatement#4", "for await(a of b);", "for await (a of b)\n    ;"),
        new("ForOfStatement#5", "for await(var a of b);", "for await (var a of b)\n    ;"),
        new("ForOfStatement#6", "for await(a of b){}", "for await (a of b) { }"),
        new("ContinueStatement#1", "continue", "continue;"),
        new("ContinueStatement#2", "continue a", "continue a;"),
        new("BreakStatement#1", "break", "break;"),
        new("BreakStatement#2", "break a", "break a;"),
        new("ReturnStatement#1", "return", "return;"),
        new("ReturnStatement#2", "return a", "return a;"),
        new("WithStatement#1", "with(a);", "with (a)\n    ;"),
        new("WithStatement#2", "with(a){}", "with (a) { }"),
        new("SwitchStatement", "switch (a) {}", "switch (a) {\n}"),
        new("CaseClause#1", "switch (a) {case b:}", "switch (a) {\n    case b:\n}"),
        new("CaseClause#2", "switch (a) {case b:;}", "switch (a) {\n    case b: ;\n}"),
        new("DefaultClause#1", "switch (a) {default:}", "switch (a) {\n    default:\n}"),
        new("DefaultClause#2", "switch (a) {default:;}", "switch (a) {\n    default: ;\n}"),
        new("LabeledStatement", "a:;", "a: ;"),
        new("ThrowStatement", "throw a", "throw a;"),
        new("TryStatement#1", "try {} catch {}", "try { }\ncatch { }"),
        new("TryStatement#2", "try {} finally {}", "try { }\nfinally { }"),
        new("TryStatement#3", "try {} catch {} finally {}", "try { }\ncatch { }\nfinally { }"),
        new("DebuggerStatement", "debugger", "debugger;"),
        new("FunctionDeclaration#1", "export default function(){}", "export default function () { }"),
        new("FunctionDeclaration#2", "function f(){}", "function f() { }"),
        new("FunctionDeclaration#3", "function*f(){}", "function* f() { }"),
        new("FunctionDeclaration#4", "async function f(){}", "async function f() { }"),
        new("FunctionDeclaration#5", "async function*f(){}", "async function* f() { }"),
        new("FunctionDeclaration#6", "function f<T>(){}", "function f<T>() { }"),
        new("FunctionDeclaration#7", "function f(a){}", "function f(a) { }"),
        new("FunctionDeclaration#8", "function f():T{}", "function f(): T { }"),
        new("FunctionDeclaration#9", "function f();", "function f();"),
        new("ClassDeclaration#1", "class a {}", "class a {\n}"),
        new("ClassDeclaration#2", "class a<T>{}", "class a<T> {\n}"),
        new("ClassDeclaration#3", "class a extends b {}", "class a extends b {\n}"),
        new("ClassDeclaration#4", "class a implements b {}", "class a implements b {\n}"),
        new("ClassDeclaration#5", "class a implements b, c {}", "class a implements b, c {\n}"),
        new("ClassDeclaration#6", "class a extends b implements c, d {}", "class a extends b implements c, d {\n}"),
        new("ClassDeclaration#7", "export default class {}", "export default class {\n}"),
        new("ClassDeclaration#8", "export default class<T>{}", "export default class<T> {\n}"),
        new("ClassDeclaration#9", "export default class extends b {}", "export default class extends b {\n}"),
        new("ClassDeclaration#10", "export default class implements b {}", "export default class implements b {\n}"),
        new("ClassDeclaration#11", "export default class implements b, c {}", "export default class implements b, c {\n}"),
        new("ClassDeclaration#12", "export default class extends b implements c, d {}", "export default class extends b implements c, d {\n}"),
        new("ClassDeclaration#13", "@a class b {}", "@a\nclass b {\n}"),
        new("ClassDeclaration#14", "@a export class b {}", "@a\nexport class b {\n}"),
        new("ClassDeclaration#15", "export @a class b {}", "export \n@a\nclass b {\n}"),
        new("InterfaceDeclaration#1", "interface a {}", "interface a {\n}"),
        new("InterfaceDeclaration#2", "interface a<T>{}", "interface a<T> {\n}"),
        new("InterfaceDeclaration#3", "interface a extends b {}", "interface a extends b {\n}"),
        new("InterfaceDeclaration#4", "interface a extends b, c {}", "interface a extends b, c {\n}"),
        new("TypeAliasDeclaration#1", "type a = b", "type a = b;"),
        new("TypeAliasDeclaration#2", "type a<T> = b", "type a<T> = b;"),
        new("EnumDeclaration#1", "enum a{}", "enum a {\n}"),
        new("EnumDeclaration#2", "enum a{b}", "enum a {\n    b\n}"),
        new("EnumDeclaration#3", "enum a{b=c}", "enum a {\n    b = c\n}"),
        new("ModuleDeclaration#1", "module a{}", "module a { }"),
        new("ModuleDeclaration#2", "module a.b{}", "module a.b { }"),
        new("ModuleDeclaration#3", "module \"a\";", "module \"a\";"),
        new("ModuleDeclaration#4", "module \"a\"{}", "module \"a\" { }"),
        new("ModuleDeclaration#5", "namespace a{}", "namespace a { }"),
        new("ModuleDeclaration#6", "namespace a.b{}", "namespace a.b { }"),
        new("ModuleDeclaration#7", "global;", "global;"),
        new("ModuleDeclaration#8", "global{}", "global { }"),
        new("ImportEqualsDeclaration#1", "import a = b", "import a = b;"),
        new("ImportEqualsDeclaration#2", "import a = b.c", "import a = b.c;"),
        new("ImportEqualsDeclaration#3", "import a = require(\"b\")", "import a = require(\"b\");"),
        new("ImportEqualsDeclaration#4", "export import a = b", "export import a = b;"),
        new("ImportEqualsDeclaration#5", "export import a = require(\"b\")", "export import a = require(\"b\");"),
        new("ImportEqualsDeclaration#6", "import type a = b", "import type a = b;"),
        new("ImportEqualsDeclaration#7", "import type a = b.c", "import type a = b.c;"),
        new("ImportEqualsDeclaration#8", "import type a = require(\"b\")", "import type a = require(\"b\");"),
        new("ImportDeclaration#1", "import \"a\"", "import \"a\";"),
        new("ImportDeclaration#2", "import a from \"b\"", "import a from \"b\";"),
        new("ImportDeclaration#3", "import type a from \"b\"", "import type a from \"b\";"),
        new("ImportDeclaration#4", "import * as a from \"b\"", "import * as a from \"b\";"),
        new("ImportDeclaration#5", "import type * as a from \"b\"", "import type * as a from \"b\";"),
        new("ImportDeclaration#6", "import {} from \"b\"", "import {} from \"b\";"),
        new("ImportDeclaration#7", "import type {} from \"b\"", "import type {} from \"b\";"),
        new("ImportDeclaration#8", "import { a } from \"b\"", "import { a } from \"b\";"),
        new("ImportDeclaration#9", "import type { a } from \"b\"", "import type { a } from \"b\";"),
        new("ImportDeclaration#8", "import { a as b } from \"c\"", "import { a as b } from \"c\";"),
        new("ImportDeclaration#9", "import type { a as b } from \"c\"", "import type { a as b } from \"c\";"),
        new("ImportDeclaration#10", "import { \"a\" as b } from \"c\"", "import { \"a\" as b } from \"c\";"),
        new("ImportDeclaration#11", "import type { \"a\" as b } from \"c\"", "import type { \"a\" as b } from \"c\";"),
        new("ImportDeclaration#12", "import a, {} from \"b\"", "import a, {} from \"b\";"),
        new("ImportDeclaration#13", "import a, * as b from \"c\"", "import a, * as b from \"c\";"),
        new("ImportDeclaration#14", "import {} from \"a\" with {}", "import {} from \"a\" with {};"),
        new("ImportDeclaration#15", "import {} from \"a\" with { b: \"c\" }", "import {} from \"a\" with { b: \"c\" };"),
        new("ImportDeclaration#16", "import {} from \"a\" with { \"b\": \"c\" }", "import {} from \"a\" with { \"b\": \"c\" };"),
        new("ExportAssignment#1", "export = a", "export = a;"),
        new("ExportAssignment#2", "export default a", "export default a;"),
        new("NamespaceExportDeclaration", "export as namespace a", "export as namespace a;"),
        new("ExportDeclaration#1", "export * from \"a\"", "export * from \"a\";"),
        new("ExportDeclaration#2", "export type * from \"a\"", "export type * from \"a\";"),
        new("ExportDeclaration#3", "export * as a from \"b\"", "export * as a from \"b\";"),
        new("ExportDeclaration#4", "export type * as a from \"b\"", "export type * as a from \"b\";"),
        new("ExportDeclaration#5", "export { } from \"a\"", "export {} from \"a\";"),
        new("ExportDeclaration#6", "export type { } from \"a\"", "export type {} from \"a\";"),
        new("ExportDeclaration#7", "export { a } from \"b\"", "export { a } from \"b\";"),
        new("ExportDeclaration#8", "export { type a } from \"b\"", "export { type a } from \"b\";"),
        new("ExportDeclaration#9", "export type { a } from \"b\"", "export type { a } from \"b\";"),
        new("ExportDeclaration#10", "export { a as b } from \"c\"", "export { a as b } from \"c\";"),
        new("ExportDeclaration#11", "export { type a as b } from \"c\"", "export { type a as b } from \"c\";"),
        new("ExportDeclaration#12", "export type { a as b } from \"c\"", "export type { a as b } from \"c\";"),
        new("ExportDeclaration#13", "export { a as \"b\" } from \"c\"", "export { a as \"b\" } from \"c\";"),
        new("ExportDeclaration#14", "export { type a as \"b\" } from \"c\"", "export { type a as \"b\" } from \"c\";"),
        new("ExportDeclaration#15", "export type { a as \"b\" } from \"c\"", "export type { a as \"b\" } from \"c\";"),
        new("ExportDeclaration#16", "export { \"a\" } from \"b\"", "export { \"a\" } from \"b\";"),
        new("ExportDeclaration#17", "export { type \"a\" } from \"b\"", "export { type \"a\" } from \"b\";"),
        new("ExportDeclaration#18", "export type { \"a\" } from \"b\"", "export type { \"a\" } from \"b\";"),
        new("ExportDeclaration#19", "export { \"a\" as b } from \"c\"", "export { \"a\" as b } from \"c\";"),
        new("ExportDeclaration#20", "export { type \"a\" as b } from \"c\"", "export { type \"a\" as b } from \"c\";"),
        new("ExportDeclaration#21", "export type { \"a\" as b } from \"c\"", "export type { \"a\" as b } from \"c\";"),
        new("ExportDeclaration#22", "export { \"a\" as \"b\" } from \"c\"", "export { \"a\" as \"b\" } from \"c\";"),
        new("ExportDeclaration#23", "export { type \"a\" as \"b\" } from \"c\"", "export { type \"a\" as \"b\" } from \"c\";"),
        new("ExportDeclaration#24", "export type { \"a\" as \"b\" } from \"c\"", "export type { \"a\" as \"b\" } from \"c\";"),
        new("ExportDeclaration#25", "export { }", "export {};"),
        new("ExportDeclaration#26", "export type { }", "export type {};"),
        new("ExportDeclaration#27", "export { a }", "export { a };"),
        new("ExportDeclaration#28", "export { type a }", "export { type a };"),
        new("ExportDeclaration#29", "export type { a }", "export type { a };"),
        new("ExportDeclaration#30", "export { a as b }", "export { a as b };"),
        new("ExportDeclaration#31", "export { type a as b }", "export { type a as b };"),
        new("ExportDeclaration#32", "export type { a as b }", "export type { a as b };"),
        new("ExportDeclaration#33", "export { a as \"b\" }", "export { a as \"b\" };"),
        new("ExportDeclaration#34", "export { type a as \"b\" }", "export { type a as \"b\" };"),
        new("ExportDeclaration#35", "export type { a as \"b\" }", "export type { a as \"b\" };"),
        new("ExportDeclaration#36", "export {} from \"a\" with {}", "export {} from \"a\" with {};"),
        new("ExportDeclaration#37", "export {} from \"a\" with { b: \"c\" }", "export {} from \"a\" with { b: \"c\" };"),
        new("ExportDeclaration#38", "export {} from \"a\" with { \"b\": \"c\" }", "export {} from \"a\" with { \"b\": \"c\" };"),
        new("KeywordTypeNode#1", "type T = any", "type T = any;"),
        new("KeywordTypeNode#2", "type T = unknown", "type T = unknown;"),
        new("KeywordTypeNode#3", "type T = never", "type T = never;"),
        new("KeywordTypeNode#4", "type T = void", "type T = void;"),
        new("KeywordTypeNode#5", "type T = undefined", "type T = undefined;"),
        new("KeywordTypeNode#6", "type T = null", "type T = null;"),
        new("KeywordTypeNode#7", "type T = object", "type T = object;"),
        new("KeywordTypeNode#8", "type T = string", "type T = string;"),
        new("KeywordTypeNode#9", "type T = symbol", "type T = symbol;"),
        new("KeywordTypeNode#10", "type T = number", "type T = number;"),
        new("KeywordTypeNode#11", "type T = bigint", "type T = bigint;"),
        new("KeywordTypeNode#12", "type T = boolean", "type T = boolean;"),
        new("KeywordTypeNode#13", "type T = intrinsic", "type T = intrinsic;"),
        new("TypePredicateNode#1", "function f(): asserts a", "function f(): asserts a;"),
        new("TypePredicateNode#2", "function f(): asserts a is b", "function f(): asserts a is b;"),
        new("TypePredicateNode#3", "function f(): asserts this", "function f(): asserts this;"),
        new("TypePredicateNode#4", "function f(): asserts this is b", "function f(): asserts this is b;"),
        new("TypeReferenceNode#1", "type T = a", "type T = a;"),
        new("TypeReferenceNode#2", "type T = a.b", "type T = a.b;"),
        new("TypeReferenceNode#3", "type T = a<U>", "type T = a<U>;"),
        new("TypeReferenceNode#4", "type T = a.b<U>", "type T = a.b<U>;"),
        new("FunctionTypeNode#1", "type T = () => a", "type T = () => a;"),
        new("FunctionTypeNode#2", "type T = <T>() => a", "type T = <T>() => a;"),
        new("FunctionTypeNode#3", "type T = (a) => b", "type T = (a) => b;"),
        new("ConstructorTypeNode#1", "type T = new () => a", "type T = new () => a;"),
        new("ConstructorTypeNode#2", "type T = new <T>() => a", "type T = new <T>() => a;"),
        new("ConstructorTypeNode#3", "type T = new (a) => b", "type T = new (a) => b;"),
        new("ConstructorTypeNode#4", "type T = abstract new () => a", "type T = abstract new () => a;"),
        new("TypeQueryNode#1", "type T = typeof a", "type T = typeof a;"),
        new("TypeQueryNode#2", "type T = typeof a.b", "type T = typeof a.b;"),
        new("TypeQueryNode#3", "type T = typeof a<U>", "type T = typeof a<U>;"),
        new("TypeLiteralNode#1", "type T = {}", "type T = {};"),
        new("TypeLiteralNode#2", "type T = {a}", "type T = {\n    a;\n};"),
        new("ArrayTypeNode", "type T = a[]", "type T = a[];"),
        new("TupleTypeNode#1", "type T = []", "type T = [\n];"),
        new("TupleTypeNode#2", "type T = [a]", "type T = [\n    a\n];"),
        new("TupleTypeNode#3", "type T = [a,]", "type T = [\n    a\n];"),
        new("RestTypeNode", "type T = [...a]", "type T = [\n    ...a\n];"),
        new("OptionalTypeNode", "type T = [a?]", "type T = [\n    a?\n];"),
        new("NamedTupleMember#1", "type T = [a: b]", "type T = [\n    a: b\n];"),
        new("NamedTupleMember#2", "type T = [a?: b]", "type T = [\n    a?: b\n];"),
        new("NamedTupleMember#3", "type T = [...a: b]", "type T = [\n    ...a: b\n];"),
        new("UnionTypeNode#1", "type T = a | b", "type T = a | b;"),
        new("UnionTypeNode#2", "type T = a | b | c", "type T = a | b | c;"),
        new("UnionTypeNode#3", "type T = | a | b", "type T = a | b;"),
        new("IntersectionTypeNode#1", "type T = a & b", "type T = a & b;"),
        new("IntersectionTypeNode#2", "type T = a & b & c", "type T = a & b & c;"),
        new("IntersectionTypeNode#3", "type T = & a & b", "type T = a & b;"),
        new("ConditionalTypeNode", "type T = a extends b ? c : d", "type T = a extends b ? c : d;"),
        new("InferTypeNode#1", "type T = a extends infer b ? c : d", "type T = a extends infer b ? c : d;"),
        new("InferTypeNode#2", "type T = a extends infer b extends c ? d : e", "type T = a extends infer b extends c ? d : e;"),
        new("ParenthesizedTypeNode", "type T = (U)", "type T = (U);"),
        new("ThisTypeNode", "type T = this", "type T = this;"),
        new("TypeOperatorNode#1", "type T = keyof U", "type T = keyof U;"),
        new("TypeOperatorNode#2", "type T = readonly U[]", "type T = readonly U[];"),
        new("TypeOperatorNode#3", "type T = unique symbol", "type T = unique symbol;"),
        new("IndexedAccessTypeNode", "type T = a[b]", "type T = a[b];"),
        new("MappedTypeNode#1", "type T = { [a in b]: c }", "type T = {\n    [a in b]: c;\n};"),
        new("MappedTypeNode#2", "type T = { [a in b as c]: d }", "type T = {\n    [a in b as c]: d;\n};"),
        new("MappedTypeNode#3", "type T = { readonly [a in b]: c }", "type T = {\n    readonly [a in b]: c;\n};"),
        new("MappedTypeNode#4", "type T = { +readonly [a in b]: c }", "type T = {\n    +readonly [a in b]: c;\n};"),
        new("MappedTypeNode#5", "type T = { -readonly [a in b]: c }", "type T = {\n    -readonly [a in b]: c;\n};"),
        new("MappedTypeNode#6", "type T = { [a in b]?: c }", "type T = {\n    [a in b]?: c;\n};"),
        new("MappedTypeNode#7", "type T = { [a in b]+?: c }", "type T = {\n    [a in b]+?: c;\n};"),
        new("MappedTypeNode#8", "type T = { [a in b]-?: c }", "type T = {\n    [a in b]-?: c;\n};"),
        new("MappedTypeNode#9", "type T = { [a in b]: c; d }", "type T = {\n    [a in b]: c;\n    d;\n};"),
        new("LiteralTypeNode#1", "type T = null", "type T = null;"),
        new("LiteralTypeNode#2", "type T = true", "type T = true;"),
        new("LiteralTypeNode#3", "type T = false", "type T = false;"),
        new("LiteralTypeNode#4", "type T = \"\"", "type T = \"\";"),
        new("LiteralTypeNode#5", "type T = ''", "type T = '';"),
        new("LiteralTypeNode#6", "type T = ``", "type T = ``;"),
        new("LiteralTypeNode#7", "type T = 0", "type T = 0;"),
        new("LiteralTypeNode#8", "type T = 0n", "type T = 0n;"),
        new("LiteralTypeNode#9", "type T = -0", "type T = -0;"),
        new("LiteralTypeNode#10", "type T = -0n", "type T = -0n;"),
        new("TemplateTypeNode#1", "type T = `a${b}c`", "type T = `a${b}c`;"),
        new("TemplateTypeNode#2", "type T = `a${b}c${d}e`", "type T = `a${b}c${d}e`;"),
        new("ImportTypeNode#1", "type T = import(a)", "type T = import(a);"),
        new("ImportTypeNode#2", "type T = import(a).b", "type T = import(a).b;"),
        new("ImportTypeNode#3", "type T = import(a).b<U>", "type T = import(a).b<U>;"),
        new("ImportTypeNode#4", "type T = typeof import(a)", "type T = typeof import(a);"),
        new("ImportTypeNode#5", "type T = typeof import(a).b", "type T = typeof import(a).b;"),
        new("ImportTypeNode#6", "type T = import(a, { with: { } })", "type T = import(a, { with: {} });"),
        new("ImportTypeNode#6", "type T = import(a, { with: { b: \"c\" } })", "type T = import(a, { with: { b: \"c\" } });"),
        new("ImportTypeNode#7", "type T = import(a, { with: { \"b\": \"c\" } })", "type T = import(a, { with: { \"b\": \"c\" } });"),
        new("PropertySignature#1", "interface I {a}", "interface I {\n    a;\n}"),
        new("PropertySignature#2", "interface I {readonly a}", "interface I {\n    readonly a;\n}"),
        new("PropertySignature#3", "interface I {\"a\"}", "interface I {\n    \"a\";\n}"),
        new("PropertySignature#4", "interface I {'a'}", "interface I {\n    'a';\n}"),
        new("PropertySignature#5", "interface I {0}", "interface I {\n    0;\n}"),
        new("PropertySignature#6", "interface I {0n}", "interface I {\n    0n;\n}"),
        new("PropertySignature#7", "interface I {[a]}", "interface I {\n    [a];\n}"),
        new("PropertySignature#8", "interface I {a?}", "interface I {\n    a?;\n}"),
        new("PropertySignature#9", "interface I {a: b}", "interface I {\n    a: b;\n}"),
        new("MethodSignature#1", "interface I {a()}", "interface I {\n    a();\n}"),
        new("MethodSignature#2", "interface I {\"a\"()}", "interface I {\n    \"a\"();\n}"),
        new("MethodSignature#3", "interface I {'a'()}", "interface I {\n    'a'();\n}"),
        new("MethodSignature#4", "interface I {0()}", "interface I {\n    0();\n}"),
        new("MethodSignature#5", "interface I {0n()}", "interface I {\n    0n();\n}"),
        new("MethodSignature#6", "interface I {[a]()}", "interface I {\n    [a]();\n}"),
        new("MethodSignature#7", "interface I {a?()}", "interface I {\n    a?();\n}"),
        new("MethodSignature#8", "interface I {a<T>()}", "interface I {\n    a<T>();\n}"),
        new("MethodSignature#9", "interface I {a(): b}", "interface I {\n    a(): b;\n}"),
        new("MethodSignature#10", "interface I {a(b): c}", "interface I {\n    a(b): c;\n}"),
        new("CallSignature#1", "interface I {()}", "interface I {\n    ();\n}"),
        new("CallSignature#2", "interface I {():a}", "interface I {\n    (): a;\n}"),
        new("CallSignature#3", "interface I {(p)}", "interface I {\n    (p);\n}"),
        new("CallSignature#4", "interface I {<T>()}", "interface I {\n    <T>();\n}"),
        new("ConstructSignature#1", "interface I {new ()}", "interface I {\n    new ();\n}"),
        new("ConstructSignature#2", "interface I {new ():a}", "interface I {\n    new (): a;\n}"),
        new("ConstructSignature#3", "interface I {new (p)}", "interface I {\n    new (p);\n}"),
        new("ConstructSignature#4", "interface I {new <T>()}", "interface I {\n    new <T>();\n}"),
        new("IndexSignatureDeclaration#1", "interface I {[a]}", "interface I {\n    [a];\n}"),
        new("IndexSignatureDeclaration#2", "interface I {[a: b]}", "interface I {\n    [a: b];\n}"),
        new("IndexSignatureDeclaration#3", "interface I {[a: b]: c}", "interface I {\n    [a: b]: c;\n}"),
        new("PropertyDeclaration#1", "class C {a}", "class C {\n    a;\n}"),
        new("PropertyDeclaration#2", "class C {readonly a}", "class C {\n    readonly a;\n}"),
        new("PropertyDeclaration#3", "class C {static a}", "class C {\n    static a;\n}"),
        new("PropertyDeclaration#4", "class C {accessor a}", "class C {\n    accessor a;\n}"),
        new("PropertyDeclaration#5", "class C {\"a\"}", "class C {\n    \"a\";\n}"),
        new("PropertyDeclaration#6", "class C {'a'}", "class C {\n    'a';\n}"),
        new("PropertyDeclaration#7", "class C {0}", "class C {\n    0;\n}"),
        new("PropertyDeclaration#8", "class C {0n}", "class C {\n    0n;\n}"),
        new("PropertyDeclaration#9", "class C {[a]}", "class C {\n    [a];\n}"),
        new("PropertyDeclaration#10", "class C {#a}", "class C {\n    #a;\n}"),
        new("PropertyDeclaration#11", "class C {a?}", "class C {\n    a?;\n}"),
        new("PropertyDeclaration#12", "class C {a!}", "class C {\n    a!;\n}"),
        new("PropertyDeclaration#13", "class C {a: b}", "class C {\n    a: b;\n}"),
        new("PropertyDeclaration#14", "class C {a = b}", "class C {\n    a = b;\n}"),
        new("PropertyDeclaration#15", "class C {@a b}", "class C {\n    @a\n    b;\n}"),
        new("MethodDeclaration#1", "class C {a()}", "class C {\n    a();\n}"),
        new("MethodDeclaration#2", "class C {\"a\"()}", "class C {\n    \"a\"();\n}"),
        new("MethodDeclaration#3", "class C {'a'()}", "class C {\n    'a'();\n}"),
        new("MethodDeclaration#4", "class C {0()}", "class C {\n    0();\n}"),
        new("MethodDeclaration#5", "class C {0n()}", "class C {\n    0n();\n}"),
        new("MethodDeclaration#6", "class C {[a]()}", "class C {\n    [a]();\n}"),
        new("MethodDeclaration#7", "class C {#a()}", "class C {\n    #a();\n}"),
        new("MethodDeclaration#8", "class C {a?()}", "class C {\n    a?();\n}"),
        new("MethodDeclaration#9", "class C {a<T>()}", "class C {\n    a<T>();\n}"),
        new("MethodDeclaration#10", "class C {a(): b}", "class C {\n    a(): b;\n}"),
        new("MethodDeclaration#11", "class C {a(b): c}", "class C {\n    a(b): c;\n}"),
        new("MethodDeclaration#12", "class C {a() {} }", "class C {\n    a() { }\n}"),
        new("MethodDeclaration#13", "class C {@a b() {} }", "class C {\n    @a\n    b() { }\n}"),
        new("MethodDeclaration#14", "class C {static a() {} }", "class C {\n    static a() { }\n}"),
        new("MethodDeclaration#15", "class C {async a() {} }", "class C {\n    async a() { }\n}"),
        new("GetAccessorDeclaration#1", "class C {get a()}", "class C {\n    get a();\n}"),
        new("GetAccessorDeclaration#2", "class C {get \"a\"()}", "class C {\n    get \"a\"();\n}"),
        new("GetAccessorDeclaration#3", "class C {get 'a'()}", "class C {\n    get 'a'();\n}"),
        new("GetAccessorDeclaration#4", "class C {get 0()}", "class C {\n    get 0();\n}"),
        new("GetAccessorDeclaration#5", "class C {get 0n()}", "class C {\n    get 0n();\n}"),
        new("GetAccessorDeclaration#6", "class C {get [a]()}", "class C {\n    get [a]();\n}"),
        new("GetAccessorDeclaration#7", "class C {get #a()}", "class C {\n    get #a();\n}"),
        new("GetAccessorDeclaration#8", "class C {get a(): b}", "class C {\n    get a(): b;\n}"),
        new("GetAccessorDeclaration#9", "class C {get a(b): c}", "class C {\n    get a(b): c;\n}"),
        new("GetAccessorDeclaration#10", "class C {get a() {} }", "class C {\n    get a() { }\n}"),
        new("GetAccessorDeclaration#11", "class C {@a get b() {} }", "class C {\n    @a\n    get b() { }\n}"),
        new("GetAccessorDeclaration#12", "class C {static get a() {} }", "class C {\n    static get a() { }\n}"),
        new("SetAccessorDeclaration#1", "class C {set a()}", "class C {\n    set a();\n}"),
        new("SetAccessorDeclaration#2", "class C {set \"a\"()}", "class C {\n    set \"a\"();\n}"),
        new("SetAccessorDeclaration#3", "class C {set 'a'()}", "class C {\n    set 'a'();\n}"),
        new("SetAccessorDeclaration#4", "class C {set 0()}", "class C {\n    set 0();\n}"),
        new("SetAccessorDeclaration#5", "class C {set 0n()}", "class C {\n    set 0n();\n}"),
        new("SetAccessorDeclaration#6", "class C {set [a]()}", "class C {\n    set [a]();\n}"),
        new("SetAccessorDeclaration#7", "class C {set #a()}", "class C {\n    set #a();\n}"),
        new("SetAccessorDeclaration#8", "class C {set a(): b}", "class C {\n    set a(): b;\n}"),
        new("SetAccessorDeclaration#9", "class C {set a(b): c}", "class C {\n    set a(b): c;\n}"),
        new("SetAccessorDeclaration#10", "class C {set a() {} }", "class C {\n    set a() { }\n}"),
        new("SetAccessorDeclaration#11", "class C {@a set b() {} }", "class C {\n    @a\n    set b() { }\n}"),
        new("SetAccessorDeclaration#12", "class C {static set a() {} }", "class C {\n    static set a() { }\n}"),
        new("ConstructorDeclaration#1", "class C {constructor()}", "class C {\n    constructor();\n}"),
        new("ConstructorDeclaration#2", "class C {constructor(): b}", "class C {\n    constructor(): b;\n}"),
        new("ConstructorDeclaration#3", "class C {constructor(b): c}", "class C {\n    constructor(b): c;\n}"),
        new("ConstructorDeclaration#4", "class C {constructor() {} }", "class C {\n    constructor() { }\n}"),
        new("ConstructorDeclaration#5", "class C {@a constructor() {} }", "class C {\n    constructor() { }\n}"),
        new("ConstructorDeclaration#6", "class C {private constructor() {} }", "class C {\n    private constructor() { }\n}"),
        new("ClassStaticBlockDeclaration", "class C {static { }}", "class C {\n    static { }\n}"),
        new("SemicolonClassElement#1", "class C {;}", "class C {\n    ;\n}"),
        new("ParameterDeclaration#1", "function f(a)", "function f(a);"),
        new("ParameterDeclaration#2", "function f(a: b)", "function f(a: b);"),
        new("ParameterDeclaration#3", "function f(a = b)", "function f(a = b);"),
        new("ParameterDeclaration#4", "function f(a?)", "function f(a?);"),
        new("ParameterDeclaration#5", "function f(...a)", "function f(...a);"),
        new("ParameterDeclaration#6", "function f(this)", "function f(this);"),
        // {title: "ParameterDeclaration#7", input: "function f(a,)", output: "function f(a,);"}, // TODO: preserve trailing comma after Strada migration
        new("ObjectBindingPattern#1", "function f({})", "function f({});"),
        new("ObjectBindingPattern#2", "function f({a})", "function f({ a });"),
        new("ObjectBindingPattern#3", "function f({a = b})", "function f({ a = b });"),
        new("ObjectBindingPattern#4", "function f({a: b})", "function f({ a: b });"),
        new("ObjectBindingPattern#5", "function f({a: b = c})", "function f({ a: b = c });"),
        new("ObjectBindingPattern#6", "function f({\"a\": b})", "function f({ \"a\": b });"),
        new("ObjectBindingPattern#7", "function f({'a': b})", "function f({ 'a': b });"),
        new("ObjectBindingPattern#8", "function f({0: b})", "function f({ 0: b });"),
        new("ObjectBindingPattern#9", "function f({[a]: b})", "function f({ [a]: b });"),
        new("ObjectBindingPattern#10", "function f({...a})", "function f({ ...a });"),
        new("ObjectBindingPattern#11", "function f({a: {}})", "function f({ a: {} });"),
        new("ObjectBindingPattern#12", "function f({a: []})", "function f({ a: [] });"),
        new("ArrayBindingPattern#1", "function f([])", "function f([]);"),
        new("ArrayBindingPattern#2", "function f([,])", "function f([,]);"),
        new("ArrayBindingPattern#3", "function f([a])", "function f([a]);"),
        new("ArrayBindingPattern#4", "function f([a, b])", "function f([a, b]);"),
        new("ArrayBindingPattern#5", "function f([a, , b])", "function f([a, , b]);"),
        new("ArrayBindingPattern#6", "function f([a = b])", "function f([a = b]);"),
        new("ArrayBindingPattern#7", "function f([...a])", "function f([...a]);"),
        new("ArrayBindingPattern#8", "function f([{}])", "function f([{}]);"),
        new("ArrayBindingPattern#9", "function f([[]])", "function f([[]]);"),
        new("TypeParameterDeclaration#1", "function f<T>();", "function f<T>();"),
        new("TypeParameterDeclaration#2", "function f<in T>();", "function f<in T>();"),
        new("TypeParameterDeclaration#3", "function f<T extends U>();", "function f<T extends U>();"),
        new("TypeParameterDeclaration#4", "function f<T = U>();", "function f<T = U>();"),
        new("TypeParameterDeclaration#5", "function f<T extends U = V>();", "function f<T extends U = V>();"),
        new("TypeParameterDeclaration#6", "function f<T, U>();", "function f<T, U>();"),
        // {title: "TypeParameterDeclaration#7", input: "function f<T,>();", output: "function f<T,>();"}, // TODO: preserve trailing comma after Strada migration
        new("JsxElement1", "<a></a>", "<a></a>;", true),
        new("JsxElement2", "<this></this>", "<this></this>;", true),
        new("JsxElement3", "<a:b></a:b>", "<a:b></a:b>;", true),
        new("JsxElement4", "<a.b></a.b>", "<a.b></a.b>;", true),
        new("JsxElement5", "<a<b>></a>", "<a<b>></a>;", true),
        new("JsxElement6", "<a b></a>", "<a b></a>;", true),
        new("JsxElement7", "<a>b</a>", "<a>b</a>;", true),
        new("JsxElement8", "<a>{b}</a>", "<a>{b}</a>;", true),
        new("JsxElement9", "<a><b></b></a>", "<a><b></b></a>;", true),
        new("JsxElement10", "<a><b /></a>", "<a><b /></a>;", true),
        new("JsxElement11", "<a><></></a>", "<a><></></a>;", true),
        new("JsxElement12", "<a>\n    {/* missing */}\n    {\n        // foo\n    }\n</a>", "<a>\n    {/* missing */}\n    {\n    // foo\n    }\n</a>;", true),
        new("JsxSelfClosingElement1", "<a />", "<a />;", true),
        new("JsxSelfClosingElement2", "<this />", "<this />;", true),
        new("JsxSelfClosingElement3", "<a:b />", "<a:b />;", true),
        new("JsxSelfClosingElement4", "<a.b />", "<a.b />;", true),
        new("JsxSelfClosingElement5", "<a<b> />", "<a<b> />;", true),
        new("JsxSelfClosingElement6", "<a b/>", "<a b/>;", true),
        new("JsxFragment1", "<></>", "<></>;", true),
        new("JsxFragment2", "<>b</>", "<>b</>;", true),
        new("JsxFragment3", "<>{b}</>", "<>{b}</>;", true),
        new("JsxFragment4", "<><b></b></>", "<><b></b></>;", true),
        new("JsxFragment5", "<><b /></>", "<><b /></>;", true),
        new("JsxFragment6", "<><></></>", "<><></></>;", true),
        new("JsxAttribute1", "<a b/>", "<a b/>;", true),
        new("JsxAttribute2", "<a b:c/>", "<a b:c/>;", true),
        new("JsxAttribute3", "<a b=\"c\"/>", "<a b=\"c\"/>;", true),
        new("JsxAttribute4", "<a b='c'/>", "<a b='c'/>;", true),
        new("JsxAttribute5", "<a b={c}/>", "<a b={c}/>;", true),
        new("JsxAttribute6", "<a b=<c></c>/>", "<a b=<c></c>/>;", true),
        new("JsxAttribute7", "<a b=<c />/>", "<a b=<c />/>;", true),
        new("JsxAttribute8", "<a b=<></>/>", "<a b=<></>/>;", true),
        new("JsxSpreadAttribute", "<a {...b}/>", "<a {...b}/>;", true),
    };

    [Theory]
    [MemberData(nameof(EmitTests))]
    public void TestEmit(EmitCase rec)
    {
        output.WriteLine(rec.Title);

        var file = ParseTestUtil.ParseTypeScript(rec.Input, rec.Jsx);
        ParseTestUtil.CheckDiagnostics(file);
        EmitTestUtil.CheckEmit(null, file, rec.Output);
    }

    // Go: TestParenthesizeDecorator。
    [Fact]
    public void TestParenthesizeDecorator()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewClassDeclaration(
                factory.NewModifierList([
                    factory.NewDecorator(
                        factory.NewBinaryExpression(
                            null, /*modifiers*/
                            factory.NewIdentifier("a"),
                            null, /*typeNode*/
                            factory.NewToken(Kind.PlusToken),
                            factory.NewIdentifier("b"))),
                ]),
                factory.NewIdentifier("C"),
                null,
                null,
                factory.NewNodeList([])),
        ]);

        EmitTestUtil.CheckEmit(null, file, "@(a + b)\nclass C {\n}");
    }

    // Go: TestParenthesizeComputedPropertyName。
    [Fact]
    public void TestParenthesizeComputedPropertyName()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewClassDeclaration(
                null, /*modifiers*/
                factory.NewIdentifier("C"),
                null, /*typeParameters*/
                null, /*heritageClauses*/
                factory.NewNodeList([
                    factory.NewPropertyDeclaration(
                        null, /*modifiers*/
                        factory.NewComputedPropertyName(
                            // will be parenthesized on emit:
                            factory.NewBinaryExpression(
                                null, /*modifiers*/
                                factory.NewIdentifier("a"),
                                null, /*typeNode*/
                                factory.NewToken(Kind.CommaToken),
                                factory.NewIdentifier("b"))),
                        null, /*postfixToken*/
                        null, /*typeNode*/
                        null /*initializer*/),
                ])),
        ]);

        EmitTestUtil.CheckEmit(null, file, "class C {\n    [(a, b)];\n}");
    }

    // Go: TestParenthesizeArrayLiteral。
    [Fact]
    public void TestParenthesizeArrayLiteral()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewArrayLiteralExpression(
                    factory.NewNodeList([
                        // will be parenthesized on emit:
                        factory.NewBinaryExpression(
                            null, /*modifiers*/
                            factory.NewIdentifier("a"),
                            null, /*typeNode*/
                            factory.NewToken(Kind.CommaToken),
                            factory.NewIdentifier("b")),
                    ]),
                    false /*multiLine*/)),
        ]);

        EmitTestUtil.CheckEmit(null, file, "[(a, b)];");
    }

    // Go: TestParenthesizePropertyAccess1。
    [Fact]
    public void TestParenthesizePropertyAccess1()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewPropertyAccessExpression(
                    // will be parenthesized on emit:
                    factory.NewBinaryExpression(
                        null, /*modifiers*/
                        factory.NewIdentifier("a"),
                        null, /*typeNode*/
                        factory.NewToken(Kind.CommaToken),
                        factory.NewIdentifier("b")),
                    null, /*questionDotToken*/
                    factory.NewIdentifier("c"),
                    NodeFlags.None)),
        ]);

        EmitTestUtil.CheckEmit(null, file, "(a, b).c;");
    }

    // Go: TestParenthesizePropertyAccess2。
    [Fact]
    public void TestParenthesizePropertyAccess2()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewPropertyAccessExpression(
                    // will be parenthesized on emit:
                    factory.NewPropertyAccessExpression(
                        factory.NewIdentifier("a"),
                        factory.NewToken(Kind.QuestionDotToken),
                        factory.NewIdentifier("b"),
                        NodeFlags.OptionalChain),
                    null, /*questionDotToken*/
                    factory.NewIdentifier("c"),
                    NodeFlags.None)),
        ]);

        EmitTestUtil.CheckEmit(null, file, "(a?.b).c;");
    }

    // Go: TestParenthesizePropertyAccess3。
    [Fact]
    public void TestParenthesizePropertyAccess3()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewPropertyAccessExpression(
                    // will be parenthesized on emit:
                    factory.NewNewExpression(
                        factory.NewIdentifier("a"),
                        null, /*typeArguments*/
                        null /*arguments*/),
                    null, /*questionDotToken*/
                    factory.NewIdentifier("b"),
                    NodeFlags.None)),
        ]);

        EmitTestUtil.CheckEmit(null, file, "(new a).b;");
    }

    // Go: TestParenthesizeElementAccess1。
    [Fact]
    public void TestParenthesizeElementAccess1()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewElementAccessExpression(
                    // will be parenthesized on emit:
                    factory.NewBinaryExpression(
                        null, /*modifiers*/
                        factory.NewIdentifier("a"),
                        null, /*typeNode*/
                        factory.NewToken(Kind.CommaToken),
                        factory.NewIdentifier("b")),
                    null, /*questionDotToken*/
                    factory.NewIdentifier("c"),
                    NodeFlags.None)),
        ]);

        EmitTestUtil.CheckEmit(null, file, "(a, b)[c];");
    }

    // Go: TestParenthesizeElementAccess2。
    [Fact]
    public void TestParenthesizeElementAccess2()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewElementAccessExpression(
                    // will be parenthesized on emit:
                    factory.NewPropertyAccessExpression(
                        factory.NewIdentifier("a"),
                        factory.NewToken(Kind.QuestionDotToken),
                        factory.NewIdentifier("b"),
                        NodeFlags.OptionalChain),
                    null, /*questionDotToken*/
                    factory.NewIdentifier("c"),
                    NodeFlags.None)),
        ]);

        EmitTestUtil.CheckEmit(null, file, "(a?.b)[c];");
    }

    // Go: TestParenthesizeElementAccess3。
    [Fact]
    public void TestParenthesizeElementAccess3()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewElementAccessExpression(
                    // will be parenthesized on emit:
                    factory.NewNewExpression(
                        factory.NewIdentifier("a"),
                        null, /*typeArguments*/
                        null /*arguments*/),
                    null, /*questionDotToken*/
                    factory.NewIdentifier("b"),
                    NodeFlags.None)),
        ]);

        EmitTestUtil.CheckEmit(null, file, "(new a)[b];");
    }

    // Go: TestParenthesizeCall1。
    [Fact]
    public void TestParenthesizeCall1()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewCallExpression(
                    // will be parenthesized on emit:
                    factory.NewBinaryExpression(
                        null, /*modifiers*/
                        factory.NewIdentifier("a"),
                        null, /*typeNode*/
                        factory.NewToken(Kind.CommaToken),
                        factory.NewIdentifier("b")),
                    null, /*questionDotToken*/
                    null, /*typeArguments*/
                    factory.NewNodeList([]),
                    NodeFlags.None)),
        ]);

        EmitTestUtil.CheckEmit(null, file, "(a, b)();");
    }

    // Go: TestParenthesizeCall2。
    [Fact]
    public void TestParenthesizeCall2()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewCallExpression(
                    // will be parenthesized on emit:
                    factory.NewPropertyAccessExpression(
                        factory.NewIdentifier("a"),
                        factory.NewToken(Kind.QuestionDotToken),
                        factory.NewIdentifier("b"),
                        NodeFlags.OptionalChain),
                    null, /*questionDotToken*/
                    null, /*typeArguments*/
                    factory.NewNodeList([]),
                    NodeFlags.None)),
        ]);

        EmitTestUtil.CheckEmit(null, file, "(a?.b)();");
    }

    // Go: TestParenthesizeCall3。
    [Fact]
    public void TestParenthesizeCall3()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewCallExpression(
                    // will be parenthesized on emit:
                    factory.NewNewExpression(
                        factory.NewIdentifier("C"),
                        null, /*typeArguments*/
                        null /*arguments*/),
                    null, /*questionDotToken*/
                    null, /*typeArguments*/
                    factory.NewNodeList([]),
                    NodeFlags.None)),
        ]);

        EmitTestUtil.CheckEmit(null, file, "(new C)();");
    }

    // Go: TestParenthesizeCall4。
    [Fact]
    public void TestParenthesizeCall4()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewCallExpression(
                    factory.NewIdentifier("a"),
                    null, /*questionDotToken*/
                    null, /*typeArguments*/
                    factory.NewNodeList([
                        factory.NewBinaryExpression(
                            null, /*modifiers*/
                            factory.NewIdentifier("b"),
                            null, /*typeNode*/
                            factory.NewToken(Kind.CommaToken),
                            factory.NewIdentifier("c")),
                    ]),
                    NodeFlags.None)),
        ]);

        EmitTestUtil.CheckEmit(null, file, "a((b, c));");
    }

    // Go: TestParenthesizeNew1。
    [Fact]
    public void TestParenthesizeNew1()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewNewExpression(
                    // will be parenthesized on emit:
                    factory.NewBinaryExpression(
                        null, /*modifiers*/
                        factory.NewIdentifier("a"),
                        null, /*typeNode*/
                        factory.NewToken(Kind.CommaToken),
                        factory.NewIdentifier("b")),
                    null, /*typeArguments*/
                    factory.NewNodeList([]))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "new (a, b)();");
    }

    // Go: TestParenthesizeNew2。
    [Fact]
    public void TestParenthesizeNew2()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewNewExpression(
                    // will be parenthesized on emit:
                    factory.NewCallExpression(
                        factory.NewIdentifier("C"),
                        null, /*questionDotToken*/
                        null, /*typeArguments*/
                        factory.NewNodeList([]),
                        NodeFlags.None),
                    null, /*typeArguments*/
                    null /*arguments*/)),
        ]);

        EmitTestUtil.CheckEmit(null, file, "new (C());");
    }

    // Go: TestParenthesizeNew3。
    [Fact]
    public void TestParenthesizeNew3()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewNewExpression(
                    factory.NewIdentifier("C"),
                    null, /*typeArguments*/
                    factory.NewNodeList([
                        factory.NewBinaryExpression(
                            null, /*modifiers*/
                            factory.NewIdentifier("a"),
                            null, /*typeNode*/
                            factory.NewToken(Kind.CommaToken),
                            factory.NewIdentifier("b")),
                    ]))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "new C((a, b));");
    }

    // Go: TestParenthesizeTaggedTemplate1。
    [Fact]
    public void TestParenthesizeTaggedTemplate1()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewTaggedTemplateExpression(
                    // will be parenthesized on emit:
                    factory.NewBinaryExpression(
                        null, /*modifiers*/
                        factory.NewIdentifier("a"),
                        null, /*typeNode*/
                        factory.NewToken(Kind.CommaToken),
                        factory.NewIdentifier("b")),
                    null, /*questionDotToken*/
                    null, /*typeArguments*/
                    factory.NewNoSubstitutionTemplateLiteral("", TokenFlags.None),
                    NodeFlags.None)),
        ]);

        EmitTestUtil.CheckEmit(null, file, "(a, b) ``;");
    }

    // Go: TestParenthesizeTaggedTemplate2。
    [Fact]
    public void TestParenthesizeTaggedTemplate2()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewTaggedTemplateExpression(
                    // will be parenthesized on emit:
                    factory.NewPropertyAccessExpression(
                        factory.NewIdentifier("a"),
                        factory.NewToken(Kind.QuestionDotToken),
                        factory.NewIdentifier("b"),
                        NodeFlags.OptionalChain),
                    null, /*questionDotToken*/
                    null, /*typeArguments*/
                    factory.NewNoSubstitutionTemplateLiteral("", TokenFlags.None),
                    NodeFlags.None)),
        ]);

        EmitTestUtil.CheckEmit(null, file, "(a?.b) ``;");
    }

    // Go: TestParenthesizeTypeAssertion1。
    [Fact]
    public void TestParenthesizeTypeAssertion1()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewTypeAssertion(
                    factory.NewTypeReferenceNode(
                        factory.NewIdentifier("T"),
                        null /*typeArguments*/),
                    // will be parenthesized on emit:
                    factory.NewBinaryExpression(
                        null, /*modifiers*/
                        factory.NewIdentifier("a"),
                        null, /*typeNode*/
                        factory.NewToken(Kind.PlusToken),
                        factory.NewIdentifier("b")))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "<T>(a + b);");
    }

    // Go: TestParenthesizeArrowFunction1。
    [Fact]
    public void TestParenthesizeArrowFunction1()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewArrowFunction(
                    null, /*modifiers*/
                    null, /*typeParameters*/
                    factory.NewNodeList([]),
                    null, /*returnType*/
                    null, /*fullSignature*/
                    factory.NewToken(Kind.EqualsGreaterThanToken),
                    // will be parenthesized on emit:
                    factory.NewObjectLiteralExpression(
                        factory.NewNodeList([]),
                        false /*multiLine*/))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "() => ({});");
    }

    // Go: TestParenthesizeArrowFunction2。
    [Fact]
    public void TestParenthesizeArrowFunction2()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewArrowFunction(
                    null, /*modifiers*/
                    null, /*typeParameters*/
                    factory.NewNodeList([]),
                    null, /*returnType*/
                    null, /*fullSignature*/
                    factory.NewToken(Kind.EqualsGreaterThanToken),
                    // will be parenthesized on emit:
                    factory.NewPropertyAccessExpression(
                        factory.NewObjectLiteralExpression(
                            factory.NewNodeList([]),
                            false /*multiLine*/),
                        null, /*questionDotToken*/
                        factory.NewIdentifier("a"),
                        NodeFlags.None))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "() => ({}.a);");
    }

    // Go: TestParenthesizeDelete。
    [Fact]
    public void TestParenthesizeDelete()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewDeleteExpression(
                    // will be parenthesized on emit:
                    factory.NewBinaryExpression(
                        null, /*modifiers*/
                        factory.NewIdentifier("a"),
                        null, /*typeNode*/
                        factory.NewToken(Kind.PlusToken),
                        factory.NewIdentifier("b")))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "delete (a + b);");
    }

    // Go: TestParenthesizeVoid。
    [Fact]
    public void TestParenthesizeVoid()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewVoidExpression(
                    // will be parenthesized on emit:
                    factory.NewBinaryExpression(
                        null, /*modifiers*/
                        factory.NewIdentifier("a"),
                        null, /*typeNode*/
                        factory.NewToken(Kind.PlusToken),
                        factory.NewIdentifier("b")))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "void (a + b);");
    }

    // Go: TestParenthesizeTypeOf。
    [Fact]
    public void TestParenthesizeTypeOf()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewTypeOfExpression(
                    // will be parenthesized on emit:
                    factory.NewBinaryExpression(
                        null, /*modifiers*/
                        factory.NewIdentifier("a"),
                        null, /*typeNode*/
                        factory.NewToken(Kind.PlusToken),
                        factory.NewIdentifier("b")))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "typeof (a + b);");
    }

    // Go: TestParenthesizeAwait。
    [Fact]
    public void TestParenthesizeAwait()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewAwaitExpression(
                    // will be parenthesized on emit:
                    factory.NewBinaryExpression(
                        null, /*modifiers*/
                        factory.NewIdentifier("a"),
                        null, /*typeNode*/
                        factory.NewToken(Kind.PlusToken),
                        factory.NewIdentifier("b")))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "await (a + b);");
    }

    // Go: isBinaryOperator。
    private static bool IsBinaryOperator(Kind token)
    {
        switch (token)
        {
            case Kind.CommaToken:
            case Kind.LessThanToken:
            case Kind.GreaterThanToken:
            case Kind.LessThanEqualsToken:
            case Kind.GreaterThanEqualsToken:
            case Kind.EqualsEqualsToken:
            case Kind.EqualsEqualsEqualsToken:
            case Kind.ExclamationEqualsToken:
            case Kind.ExclamationEqualsEqualsToken:
            case Kind.PlusToken:
            case Kind.MinusToken:
            case Kind.AsteriskToken:
            case Kind.AsteriskAsteriskToken:
            case Kind.SlashToken:
            case Kind.PercentToken:
            case Kind.LessThanLessThanToken:
            case Kind.GreaterThanGreaterThanToken:
            case Kind.GreaterThanGreaterThanGreaterThanToken:
            case Kind.AmpersandToken:
            case Kind.BarToken:
            case Kind.CaretToken:
            case Kind.AmpersandAmpersandToken:
            case Kind.BarBarToken:
            case Kind.QuestionQuestionToken:
            case Kind.EqualsToken:
            case Kind.PlusEqualsToken:
            case Kind.MinusEqualsToken:
            case Kind.AsteriskEqualsToken:
            case Kind.AsteriskAsteriskEqualsToken:
            case Kind.SlashEqualsToken:
            case Kind.PercentEqualsToken:
            case Kind.LessThanLessThanEqualsToken:
            case Kind.GreaterThanGreaterThanEqualsToken:
            case Kind.GreaterThanGreaterThanGreaterThanEqualsToken:
            case Kind.AmpersandEqualsToken:
            case Kind.BarEqualsToken:
            case Kind.BarBarEqualsToken:
            case Kind.AmpersandAmpersandEqualsToken:
            case Kind.QuestionQuestionEqualsToken:
            case Kind.CaretEqualsToken:
            case Kind.InKeyword:
            case Kind.InstanceOfKeyword:
                return true;
        }

        return false;
    }

    // Go: makeSide。
    private static Node MakeSide(string label, Kind kind, AstNodeFactory factory)
    {
        if (kind == Kind.Identifier || kind == Kind.Unknown)
        {
            return factory.NewIdentifier(label);
        }

        if (kind == Kind.ArrowFunction)
        {
            return factory.NewArrowFunction(
                null, /*modifiers*/
                null, /*typeParameters*/
                factory.NewNodeList([]),
                null, /*returnType*/
                null, /*fullSignature*/
                factory.NewToken(Kind.EqualsGreaterThanToken),
                factory.NewBlock(factory.NewNodeList([]), false /*multiLine*/));
        }

        if (IsBinaryOperator(kind))
        {
            return factory.NewBinaryExpression(
                null, /*modifiers*/
                factory.NewIdentifier(label + "l"),
                null, /*typeNode*/
                factory.NewToken(kind),
                factory.NewIdentifier(label + "r"));
        }

        // Go: panic("unsupported kind")
        throw new DebugFailureException("unsupported kind");
    }

    public sealed record ParenthesizeBinaryCase(Kind Left, Kind Operator, Kind Right, string Output);

    public static TheoryData<ParenthesizeBinaryCase> ParenthesizeBinaryTests => new()
    {
        new(Kind.Unknown, Kind.CommaToken, Kind.Unknown, "l, r"),
        new(Kind.PlusToken, Kind.CommaToken, Kind.Unknown, "ll + lr, r"),
        new(Kind.PlusToken, Kind.AsteriskToken, Kind.Unknown, "(ll + lr) * r"),
        new(Kind.Unknown, Kind.AsteriskToken, Kind.PlusToken, "l * (rl + rr)"),
        new(Kind.AsteriskToken, Kind.PlusToken, Kind.Unknown, "ll * lr + r"),
        new(Kind.Unknown, Kind.PlusToken, Kind.AsteriskToken, "l + rl * rr"),
        new(Kind.AsteriskToken, Kind.SlashToken, Kind.Unknown, "ll * lr / r"),
        new(Kind.AsteriskAsteriskToken, Kind.SlashToken, Kind.Unknown, "ll ** lr / r"),
        new(Kind.AsteriskToken, Kind.AsteriskAsteriskToken, Kind.Unknown, "(ll * lr) ** r"),
        new(Kind.AsteriskAsteriskToken, Kind.AsteriskAsteriskToken, Kind.Unknown, "(ll ** lr) ** r"),
        new(Kind.Unknown, Kind.AsteriskToken, Kind.AsteriskToken, "l * rl * rr"),
        new(Kind.Unknown, Kind.BarToken, Kind.BarToken, "l | rl | rr"),
        new(Kind.Unknown, Kind.AmpersandToken, Kind.AmpersandToken, "l & rl & rr"),
        new(Kind.Unknown, Kind.CaretToken, Kind.CaretToken, "l ^ rl ^ rr"),
        new(Kind.Unknown, Kind.AmpersandAmpersandToken, Kind.ArrowFunction, "l && (() => { })"),
    };

    [Theory]
    [MemberData(nameof(ParenthesizeBinaryTests))]
    public void TestParenthesizeBinary(ParenthesizeBinaryCase rec)
    {
        output.WriteLine(rec.Output);

        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewBinaryExpression(
                    null, /*modifiers*/
                    MakeSide("l", rec.Left, factory),
                    null, /*typeNode*/
                    factory.NewToken(rec.Operator),
                    MakeSide("r", rec.Right, factory))),
        ]);

        EmitTestUtil.CheckEmit(null, file, rec.Output + ";");
    }

    // Go: TestParenthesizeConditional1。
    [Fact]
    public void TestParenthesizeConditional1()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewConditionalExpression(
                    // will be parenthesized on emit:
                    factory.NewBinaryExpression(
                        null, /*modifiers*/
                        factory.NewIdentifier("a"),
                        null, /*typeNode*/
                        factory.NewToken(Kind.CommaToken),
                        factory.NewIdentifier("b")),
                    factory.NewToken(Kind.QuestionToken),
                    factory.NewIdentifier("c"),
                    factory.NewToken(Kind.ColonToken),
                    factory.NewIdentifier("d"))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "(a, b) ? c : d;");
    }

    // Go: TestParenthesizeConditional2。
    [Fact]
    public void TestParenthesizeConditional2()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewConditionalExpression(
                    // will be parenthesized on emit:
                    factory.NewBinaryExpression(
                        null, /*modifiers*/
                        factory.NewIdentifier("a"),
                        null, /*typeNode*/
                        factory.NewToken(Kind.EqualsToken),
                        factory.NewIdentifier("b")),
                    factory.NewToken(Kind.QuestionToken),
                    factory.NewIdentifier("c"),
                    factory.NewToken(Kind.ColonToken),
                    factory.NewIdentifier("d"))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "(a = b) ? c : d;");
    }

    // Go: TestParenthesizeConditional3。
    [Fact]
    public void TestParenthesizeConditional3()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewConditionalExpression(
                    // will be parenthesized on emit:
                    factory.NewArrowFunction(
                        null, /*modifiers*/
                        null, /*typeParameters*/
                        factory.NewNodeList([]),
                        null, /*returnType*/
                        null, /*fullSignature*/
                        factory.NewToken(Kind.EqualsGreaterThanToken),
                        factory.NewBlock(
                            factory.NewNodeList([]),
                            false /*multiLine*/)),
                    factory.NewToken(Kind.QuestionToken),
                    factory.NewIdentifier("a"),
                    factory.NewToken(Kind.ColonToken),
                    factory.NewIdentifier("b"))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "(() => { }) ? a : b;");
    }

    // Go: TestParenthesizeConditional4。
    [Fact]
    public void TestParenthesizeConditional4()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewConditionalExpression(
                    // will be parenthesized on emit:
                    factory.NewYieldExpression(null, null),
                    factory.NewToken(Kind.QuestionToken),
                    factory.NewIdentifier("a"),
                    factory.NewToken(Kind.ColonToken),
                    factory.NewIdentifier("b"))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "(yield) ? a : b;");
    }

    // Go: TestParenthesizeConditional5。
    [Fact]
    public void TestParenthesizeConditional5()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewConditionalExpression(
                    factory.NewIdentifier("a"),
                    factory.NewToken(Kind.QuestionToken),
                    // will be parenthesized on emit:
                    factory.NewBinaryExpression(
                        null, /*modifiers*/
                        factory.NewIdentifier("b"),
                        null, /*typeNode*/
                        factory.NewToken(Kind.CommaToken),
                        factory.NewIdentifier("c")),
                    factory.NewToken(Kind.ColonToken),
                    factory.NewIdentifier("d"))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "a ? (b, c) : d;");
    }

    // Go: TestParenthesizeConditional6。
    [Fact]
    public void TestParenthesizeConditional6()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewConditionalExpression(
                    factory.NewIdentifier("a"),
                    factory.NewToken(Kind.QuestionToken),
                    factory.NewIdentifier("b"),
                    factory.NewToken(Kind.ColonToken),
                    // will be parenthesized on emit:
                    factory.NewBinaryExpression(
                        null, /*modifiers*/
                        factory.NewIdentifier("c"),
                        null, /*typeNode*/
                        factory.NewToken(Kind.CommaToken),
                        factory.NewIdentifier("d")))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "a ? b : (c, d);");
    }

    // Go: TestParenthesizeYield1。
    [Fact]
    public void TestParenthesizeYield1()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewYieldExpression(
                    null, /*asteriskToken*/
                    // will be parenthesized on emit:
                    factory.NewBinaryExpression(
                        null, /*modifiers*/
                        factory.NewIdentifier("a"),
                        null, /*typeNode*/
                        factory.NewToken(Kind.CommaToken),
                        factory.NewIdentifier("b")))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "yield (a, b);");
    }

    // !!! test ASI avoidance from emitExpressionNoASI
    //// Go: TestParenthesizeYield2（Go 侧本身就是注释掉的空函数，这里照原样保留）。
    //// func TestParenthesizeYield2(t *testing.T) { }

    // Go: TestParenthesizeSpreadElement1。
    [Fact]
    public void TestParenthesizeSpreadElement1()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewArrayLiteralExpression(
                    factory.NewNodeList([
                        factory.NewSpreadElement(
                            // will be parenthesized on emit:
                            factory.NewBinaryExpression(
                                null, /*modifiers*/
                                factory.NewIdentifier("a"),
                                null, /*typeNode*/
                                factory.NewToken(Kind.CommaToken),
                                factory.NewIdentifier("b"))),
                    ]),
                    false /*multiLine*/)),
        ]);

        EmitTestUtil.CheckEmit(null, file, "[...(a, b)];");
    }

    // Go: TestParenthesizeSpreadElement2。
    [Fact]
    public void TestParenthesizeSpreadElement2()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewCallExpression(
                    factory.NewIdentifier("a"),
                    null, /*questionDotToken*/
                    null, /*typeArguments*/
                    factory.NewNodeList([
                        factory.NewSpreadElement(
                            // will be parenthesized on emit:
                            factory.NewBinaryExpression(
                                null, /*modifiers*/
                                factory.NewIdentifier("b"),
                                null, /*typeNode*/
                                factory.NewToken(Kind.CommaToken),
                                factory.NewIdentifier("c"))),
                    ]),
                    NodeFlags.None)),
        ]);

        EmitTestUtil.CheckEmit(null, file, "a(...(b, c));");
    }

    // Go: TestParenthesizeSpreadElement3。
    [Fact]
    public void TestParenthesizeSpreadElement3()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewNewExpression(
                    factory.NewIdentifier("a"),
                    null, /*typeArguments*/
                    factory.NewNodeList([
                        factory.NewSpreadElement(
                            // will be parenthesized on emit:
                            factory.NewBinaryExpression(
                                null, /*modifiers*/
                                factory.NewIdentifier("b"),
                                null, /*typeNode*/
                                factory.NewToken(Kind.CommaToken),
                                factory.NewIdentifier("c"))),
                    ]))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "new a(...(b, c));");
    }

    // Go: TestParenthesizeExpressionWithTypeArguments。
    [Fact]
    public void TestParenthesizeExpressionWithTypeArguments()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewExpressionWithTypeArguments(
                    // will be parenthesized on emit:
                    factory.NewBinaryExpression(
                        null, /*modifiers*/
                        factory.NewIdentifier("a"),
                        null, /*typeNode*/
                        factory.NewToken(Kind.CommaToken),
                        factory.NewIdentifier("b")),
                    factory.NewNodeList([
                        factory.NewTypeReferenceNode(
                            factory.NewIdentifier("c"),
                            null),
                    ]))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "(a, b)<c>;");
    }

    // Go: TestParenthesizeAsExpression。
    [Fact]
    public void TestParenthesizeAsExpression()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewAsExpression(
                    // will be parenthesized on emit:
                    factory.NewBinaryExpression(
                        null, /*modifiers*/
                        factory.NewIdentifier("a"),
                        null, /*typeNode*/
                        factory.NewToken(Kind.CommaToken),
                        factory.NewIdentifier("b")),
                    factory.NewTypeReferenceNode(
                        factory.NewIdentifier("c"),
                        null))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "(a, b) as c;");
    }

    // Go: TestParenthesizeSatisfiesExpression。
    [Fact]
    public void TestParenthesizeSatisfiesExpression()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewSatisfiesExpression(
                    // will be parenthesized on emit:
                    factory.NewBinaryExpression(
                        null, /*modifiers*/
                        factory.NewIdentifier("a"),
                        null, /*typeNode*/
                        factory.NewToken(Kind.CommaToken),
                        factory.NewIdentifier("b")),
                    factory.NewTypeReferenceNode(
                        factory.NewIdentifier("c"),
                        null))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "(a, b) satisfies c;");
    }

    // Go: TestParenthesizeNonNullExpression。
    [Fact]
    public void TestParenthesizeNonNullExpression()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewNonNullExpression(
                    // will be parenthesized on emit:
                    factory.NewBinaryExpression(
                        null, /*modifiers*/
                        factory.NewIdentifier("a"),
                        null, /*typeNode*/
                        factory.NewToken(Kind.CommaToken),
                        factory.NewIdentifier("b")),
                    NodeFlags.None)),
        ]);

        EmitTestUtil.CheckEmit(null, file, "(a, b)!;");
    }

    // Go: TestParenthesizeExpressionStatement1。
    [Fact]
    public void TestParenthesizeExpressionStatement1()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewObjectLiteralExpression(
                    factory.NewNodeList([]),
                    false /*multiLine*/)),
        ]);

        EmitTestUtil.CheckEmit(null, file, "({});");
    }

    // Go: TestParenthesizeExpressionStatement2。
    [Fact]
    public void TestParenthesizeExpressionStatement2()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewFunctionExpression(
                    null, /*modifiers*/
                    null, /*asteriskToken*/
                    null, /*name*/
                    null, /*typeParameters*/
                    factory.NewNodeList([]),
                    null, /*returnType*/
                    null, /*fullSignature*/
                    factory.NewBlock(
                        factory.NewNodeList([]),
                        false /*multiLine*/))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "(function () { });");
    }

    // Go: TestParenthesizeExpressionStatement3。
    [Fact]
    public void TestParenthesizeExpressionStatement3()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(
                factory.NewClassExpression(
                    null, /*modifiers*/
                    null, /*name*/
                    null, /*typeParameters*/
                    null, /*heritageClauses*/
                    factory.NewNodeList([]))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "class {\n};");
    }

    // Go: TestParenthesizeExpressionDefault1。
    [Fact]
    public void TestParenthesizeExpressionDefault1()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExportAssignment(
                null,  /*modifiers*/
                false, /*isExportEquals*/
                null,  /*typeNode*/
                // will be parenthesized on emit:
                factory.NewClassExpression(
                    null, /*modifiers*/
                    null, /*name*/
                    null, /*typeParameters*/
                    null, /*heritageClauses*/
                    factory.NewNodeList([]))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "export default (class {\n});");
    }

    // Go: TestParenthesizeExpressionDefault2。
    [Fact]
    public void TestParenthesizeExpressionDefault2()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExportAssignment(
                null,  /*modifiers*/
                false, /*isExportEquals*/
                null,  /*typeNode*/
                // will be parenthesized on emit:
                factory.NewFunctionExpression(
                    null, /*modifiers*/
                    null, /*asteriskToken*/
                    null, /*name*/
                    null, /*typeParameters*/
                    factory.NewNodeList([]),
                    null, /*returnType*/
                    null, /*fullSignature*/
                    factory.NewBlock(
                        factory.NewNodeList([]),
                        false /*multiLine*/))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "export default (function () { });");
    }

    // Go: TestParenthesizeExpressionDefault3。
    [Fact]
    public void TestParenthesizeExpressionDefault3()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewExportAssignment(
                null,  /*modifiers*/
                false, /*isExportEquals*/
                null,  /*typeNode*/
                // will be parenthesized on emit:
                factory.NewBinaryExpression(
                    null, /*modifiers*/
                    factory.NewIdentifier("a"),
                    null, /*typeNode*/
                    factory.NewToken(Kind.CommaToken),
                    factory.NewIdentifier("b"))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "export default (a, b);");
    }

    // Go: TestParenthesizeArrayType。
    [Fact]
    public void TestParenthesizeArrayType()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewTypeAliasDeclaration(
                null,                       /*modifiers*/
                factory.NewIdentifier("_"), /*name*/
                null,                       /*typeParameters*/
                factory.NewArrayTypeNode(
                    // will be parenthesized on emit:
                    factory.NewUnionTypeNode(
                        factory.NewNodeList([
                            factory.NewTypeReferenceNode(factory.NewIdentifier("a"), null /*typeArguments*/),
                            factory.NewTypeReferenceNode(factory.NewIdentifier("b"), null /*typeArguments*/),
                        ])))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "type _ = (a | b)[];");
    }

    // Go: TestParenthesizeOptionalType。
    [Fact]
    public void TestParenthesizeOptionalType()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewTypeAliasDeclaration(
                null,                       /*modifiers*/
                factory.NewIdentifier("_"), /*name*/
                null,                       /*typeParameters*/
                factory.NewTupleTypeNode(
                    factory.NewNodeList([
                        factory.NewOptionalTypeNode(
                            // will be parenthesized on emit:
                            factory.NewUnionTypeNode(
                                factory.NewNodeList([
                                    factory.NewTypeReferenceNode(factory.NewIdentifier("a"), null /*typeArguments*/),
                                    factory.NewTypeReferenceNode(factory.NewIdentifier("b"), null /*typeArguments*/),
                                ]))),
                    ]))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "type _ = [\n    (a | b)?\n];");
    }

    // Go: TestParenthesizeUnionType1。
    [Fact]
    public void TestParenthesizeUnionType1()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewTypeAliasDeclaration(
                null,                       /*modifiers*/
                factory.NewIdentifier("_"), /*name*/
                null,                       /*typeParameters*/
                factory.NewUnionTypeNode(
                    factory.NewNodeList([
                        factory.NewTypeReferenceNode(factory.NewIdentifier("a"), null /*typeArguments*/),
                        // will be parenthesized on emit:
                        factory.NewFunctionTypeNode(
                            null, /*typeParameters*/
                            factory.NewNodeList([]),
                            factory.NewTypeReferenceNode(factory.NewIdentifier("b"), null /*typeArguments*/)),
                    ]))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "type _ = a | (() => b);");
    }

    // Go: TestParenthesizeUnionType2。
    [Fact]
    public void TestParenthesizeUnionType2()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewTypeAliasDeclaration(
                null,                       /*modifiers*/
                factory.NewIdentifier("_"), /*name*/
                null,                       /*typeParameters*/
                factory.NewUnionTypeNode(
                    factory.NewNodeList([
                        // will be parenthesized on emit:
                        factory.NewInferTypeNode(
                            factory.NewTypeParameterDeclaration(
                                null,
                                factory.NewIdentifier("a"),
                                factory.NewTypeReferenceNode(factory.NewIdentifier("b"), null /*typeArguments*/),
                                null, /*expression*/
                                null /*defaultType*/)),
                        factory.NewTypeReferenceNode(factory.NewIdentifier("c"), null /*typeArguments*/),
                    ]))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "type _ = (infer a extends b) | c;");
    }

    // Go: TestParenthesizeIntersectionType。
    [Fact]
    public void TestParenthesizeIntersectionType()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewTypeAliasDeclaration(
                null,                       /*modifiers*/
                factory.NewIdentifier("_"), /*name*/
                null,                       /*typeParameters*/
                factory.NewIntersectionTypeNode(
                    factory.NewNodeList([
                        factory.NewTypeReferenceNode(factory.NewIdentifier("a"), null /*typeArguments*/),
                        // will be parenthesized on emit:
                        factory.NewUnionTypeNode(
                            factory.NewNodeList([
                                factory.NewTypeReferenceNode(factory.NewIdentifier("b"), null /*typeArguments*/),
                                factory.NewTypeReferenceNode(factory.NewIdentifier("c"), null /*typeArguments*/),
                            ])),
                    ]))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "type _ = a & (b | c);");
    }

    // Go: TestParenthesizeReadonlyTypeOperator1。
    [Fact]
    public void TestParenthesizeReadonlyTypeOperator1()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewTypeAliasDeclaration(
                null,                       /*modifiers*/
                factory.NewIdentifier("_"), /*name*/
                null,                       /*typeParameters*/
                factory.NewTypeOperatorNode(
                    Kind.ReadonlyKeyword,
                    // will be parenthesized on emit:
                    factory.NewUnionTypeNode(
                        factory.NewNodeList([
                            factory.NewTypeReferenceNode(factory.NewIdentifier("a"), null /*typeArguments*/),
                            factory.NewTypeReferenceNode(factory.NewIdentifier("b"), null /*typeArguments*/),
                        ])))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "type _ = readonly (a | b);");
    }

    // Go: TestParenthesizeReadonlyTypeOperator2。
    [Fact]
    public void TestParenthesizeReadonlyTypeOperator2()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewTypeAliasDeclaration(
                null,                       /*modifiers*/
                factory.NewIdentifier("_"), /*name*/
                null,                       /*typeParameters*/
                factory.NewTypeOperatorNode(
                    Kind.ReadonlyKeyword,
                    // will be parenthesized on emit:
                    factory.NewTypeOperatorNode(
                        Kind.KeyOfKeyword,
                        factory.NewTypeReferenceNode(factory.NewIdentifier("a"), null /*typeArguments*/)))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "type _ = readonly (keyof a);");
    }

    // Go: TestParenthesizeKeyofTypeOperator。
    [Fact]
    public void TestParenthesizeKeyofTypeOperator()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewTypeAliasDeclaration(
                null,                       /*modifiers*/
                factory.NewIdentifier("_"), /*name*/
                null,                       /*typeParameters*/
                factory.NewTypeOperatorNode(
                    Kind.KeyOfKeyword,
                    // will be parenthesized on emit:
                    factory.NewUnionTypeNode(
                        factory.NewNodeList([
                            factory.NewTypeReferenceNode(factory.NewIdentifier("a"), null /*typeArguments*/),
                            factory.NewTypeReferenceNode(factory.NewIdentifier("b"), null /*typeArguments*/),
                        ])))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "type _ = keyof (a | b);");
    }

    // Go: TestParenthesizeIndexedAccessType。
    [Fact]
    public void TestParenthesizeIndexedAccessType()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewTypeAliasDeclaration(
                null,                       /*modifiers*/
                factory.NewIdentifier("_"), /*name*/
                null,                       /*typeParameters*/
                factory.NewIndexedAccessTypeNode(
                    // will be parenthesized on emit:
                    factory.NewUnionTypeNode(
                        factory.NewNodeList([
                            factory.NewTypeReferenceNode(factory.NewIdentifier("a"), null /*typeArguments*/),
                            factory.NewTypeReferenceNode(factory.NewIdentifier("b"), null /*typeArguments*/),
                        ])),
                    factory.NewTypeReferenceNode(factory.NewIdentifier("c"), null /*typeArguments*/))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "type _ = (a | b)[c];");
    }

    // Go: TestParenthesizeConditionalType1。
    [Fact]
    public void TestParenthesizeConditionalType1()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewTypeAliasDeclaration(
                null,                       /*modifiers*/
                factory.NewIdentifier("_"), /*name*/
                null,                       /*typeParameters*/
                factory.NewConditionalTypeNode(
                    // will be parenthesized on emit:
                    factory.NewFunctionTypeNode(
                        null, /*typeParameters*/
                        factory.NewNodeList([]),
                        factory.NewTypeReferenceNode(factory.NewIdentifier("a"), null /*typeArguments*/)),
                    factory.NewTypeReferenceNode(factory.NewIdentifier("b"), null /*typeArguments*/),
                    factory.NewTypeReferenceNode(factory.NewIdentifier("c"), null /*typeArguments*/),
                    factory.NewTypeReferenceNode(factory.NewIdentifier("d"), null /*typeArguments*/))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "type _ = (() => a) extends b ? c : d;");
    }

    // Go: TestParenthesizeConditionalType2。
    [Fact]
    public void TestParenthesizeConditionalType2()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewTypeAliasDeclaration(
                null,                       /*modifiers*/
                factory.NewIdentifier("_"), /*name*/
                null,                       /*typeParameters*/
                factory.NewConditionalTypeNode(
                    factory.NewTypeReferenceNode(factory.NewIdentifier("a"), null /*typeArguments*/),
                    // will be parenthesized on emit:
                    factory.NewConditionalTypeNode(
                        factory.NewTypeReferenceNode(factory.NewIdentifier("b"), null /*typeArguments*/),
                        factory.NewTypeReferenceNode(factory.NewIdentifier("c"), null /*typeArguments*/),
                        factory.NewTypeReferenceNode(factory.NewIdentifier("d"), null /*typeArguments*/),
                        factory.NewTypeReferenceNode(factory.NewIdentifier("e"), null /*typeArguments*/)),
                    factory.NewTypeReferenceNode(factory.NewIdentifier("f"), null /*typeArguments*/),
                    factory.NewTypeReferenceNode(factory.NewIdentifier("g"), null /*typeArguments*/))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "type _ = a extends (b extends c ? d : e) ? f : g;");
    }

    // Go: TestParenthesizeConditionalType3。
    [Fact]
    public void TestParenthesizeConditionalType3()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewTypeAliasDeclaration(
                null,                       /*modifiers*/
                factory.NewIdentifier("_"), /*name*/
                null,                       /*typeParameters*/
                factory.NewConditionalTypeNode(
                    factory.NewTypeReferenceNode(factory.NewIdentifier("a"), null /*typeArguments*/),
                    factory.NewFunctionTypeNode(
                        null, /*typeParameters*/
                        factory.NewNodeList([]),
                        // will be parenthesized on emit:
                        factory.NewInferTypeNode(
                            factory.NewTypeParameterDeclaration(
                                null,
                                factory.NewIdentifier("b"),
                                factory.NewTypeReferenceNode(factory.NewIdentifier("c"), null /*typeArguments*/),
                                null, /*expression*/
                                null /*defaultType*/))),
                    factory.NewTypeReferenceNode(factory.NewIdentifier("d"), null /*typeArguments*/),
                    factory.NewTypeReferenceNode(factory.NewIdentifier("e"), null /*typeArguments*/))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "type _ = a extends () => (infer b extends c) ? d : e;");
    }

    // Go: TestParenthesizeConditionalType4。
    [Fact]
    public void TestParenthesizeConditionalType4()
    {
        var factory = new AstNodeFactory();
        var file = NewSyntheticFile(factory, [
            factory.NewTypeAliasDeclaration(
                null,                       /*modifiers*/
                factory.NewIdentifier("_"), /*name*/
                null,                       /*typeParameters*/
                factory.NewConditionalTypeNode(
                    factory.NewTypeReferenceNode(factory.NewIdentifier("a"), null /*typeArguments*/),
                    factory.NewFunctionTypeNode(
                        null, /*typeParameters*/
                        factory.NewNodeList([]),
                        // will be parenthesized on emit:
                        factory.NewUnionTypeNode(
                            factory.NewNodeList([
                                factory.NewInferTypeNode(
                                    factory.NewTypeParameterDeclaration(
                                        null,
                                        factory.NewIdentifier("b"),
                                        factory.NewTypeReferenceNode(factory.NewIdentifier("c"), null /*typeArguments*/),
                                        null, /*expression*/
                                        null /*defaultType*/)),
                                factory.NewTypeReferenceNode(factory.NewIdentifier("d"), null /*typeArguments*/),
                            ]))),
                    factory.NewTypeReferenceNode(factory.NewIdentifier("e"), null /*typeArguments*/),
                    factory.NewTypeReferenceNode(factory.NewIdentifier("f"), null /*typeArguments*/))),
        ]);

        EmitTestUtil.CheckEmit(null, file, "type _ = a extends () => (infer b extends c) | d ? e : f;");
    }

    // Go: TestNameGeneration。这里不能用 NewSyntheticFile：Go 用 `ec.Factory`（printer.NodeFactory，
    // 带 emitContext）而不是 ast 的零值工厂。
    [Fact]
    public void TestNameGeneration()
    {
        var ec = PrinterClass.NewEmitContext();
        var file = ec.Factory.NewSourceFile(
            new SourceFileParseOptions { FileName = "/file.ts", Path = new Path("/file.ts") },
            "",
            ec.Factory.NewNodeList([
                ec.Factory.NewVariableStatement(null, ec.Factory.NewVariableDeclarationList(
                    ec.Factory.NewNodeList([
                        ec.Factory.NewVariableDeclaration(ec.Factory.NewTempVariable(), null, null, null),
                    ]),
                    NodeFlags.None)),
                ec.Factory.NewFunctionDeclaration(
                    null,
                    null,
                    ec.Factory.NewIdentifier("f"),
                    null,
                    ec.Factory.NewNodeList([]),
                    null,
                    null,
                    ec.Factory.NewBlock(ec.Factory.NewNodeList([
                        ec.Factory.NewVariableStatement(null, ec.Factory.NewVariableDeclarationList(
                            ec.Factory.NewNodeList([
                                ec.Factory.NewVariableDeclaration(ec.Factory.NewTempVariable(), null, null, null),
                            ]),
                            NodeFlags.None)),
                    ]), true)),
            ]),
            ec.Factory.NewToken(Kind.EndOfFile));

        ParseTestUtil.MarkSyntheticRecursive(file);
        EmitTestUtil.CheckEmit(ec, file.AsSourceFile()!, "var _a;\nfunction f() {\n    var _a;\n}");
    }

    // Go: TestNoTrailingCommaAfterTransform。
    [Fact]
    public void TestNoTrailingCommaAfterTransform()
    {
        var file = ParseTestUtil.ParseTypeScript("[a!]", false /*jsx*/);
        var emitContext = PrinterClass.NewEmitContext();

        NodeVisitor visitor = null!;
        visitor = emitContext.NewNodeVisitor(node =>
        {
            switch (node.Kind)
            {
                case Kind.NonNullExpression:
                    return node.Expression()!;
                default:
                    return node.VisitEachChild(visitor);
            }
        });
        file = visitor.VisitSourceFile(file);

        EmitTestUtil.CheckEmit(emitContext, file, "[a];");
    }

    // Go: TestTrailingCommaAfterTransform。
    [Fact]
    public void TestTrailingCommaAfterTransform()
    {
        var file = ParseTestUtil.ParseTypeScript("[a!,]", false /*jsx*/);
        var emitContext = PrinterClass.NewEmitContext();

        NodeVisitor visitor = null!;
        visitor = emitContext.NewNodeVisitor(node =>
        {
            switch (node.Kind)
            {
                case Kind.NonNullExpression:
                    return node.Expression()!;
                default:
                    return node.VisitEachChild(visitor);
            }
        });
        file = visitor.VisitSourceFile(file);

        EmitTestUtil.CheckEmit(emitContext, file, "[a,];");
    }

    // 未移植：Go 的 TestPartiallyEmittedExpression（printer_test.go:2509）需要 `internal/transformers`
    // （transformers.TransformOptions）与 `internal/transformers/tstransforms`（NewTypeEraserTransformer），
    // 这两个包尚未移植 —— 不写占位用例。

    // Go: TestParenthesizeBinaryExpressionMixingNullishCoalescing 的表记录。
    public sealed record NullishCoalescingCase(string Title, Kind InnerOp, Kind OuterOp, string Side, string Output);

    public static TheoryData<NullishCoalescingCase> NullishCoalescingTests => new()
    {
        // inner ?? on left side of || or &&
        new("BarBarWithLeftQuestionQuestion", Kind.QuestionQuestionToken, Kind.BarBarToken, "left", "(a ?? b) || c;"),
        new("AmpersandAmpersandWithLeftQuestionQuestion", Kind.QuestionQuestionToken, Kind.AmpersandAmpersandToken, "left", "(a ?? b) && c;"),
        // inner ?? on right side of || or &&
        new("BarBarWithRightQuestionQuestion", Kind.QuestionQuestionToken, Kind.BarBarToken, "right", "a || (b ?? c);"),
        new("AmpersandAmpersandWithRightQuestionQuestion", Kind.QuestionQuestionToken, Kind.AmpersandAmpersandToken, "right", "a && (b ?? c);"),
        // inner || or && on left side of ??
        new("QuestionQuestionWithLeftBarBar", Kind.BarBarToken, Kind.QuestionQuestionToken, "left", "(a || b) ?? c;"),
        new("QuestionQuestionWithLeftAmpersandAmpersand", Kind.AmpersandAmpersandToken, Kind.QuestionQuestionToken, "left", "(a && b) ?? c;"),
        // inner || or && on right side of ??
        new("QuestionQuestionWithRightBarBar", Kind.BarBarToken, Kind.QuestionQuestionToken, "right", "a ?? (b || c);"),
        new("QuestionQuestionWithRightAmpersandAmpersand", Kind.AmpersandAmpersandToken, Kind.QuestionQuestionToken, "right", "a ?? (b && c);"),
    };

    [Theory]
    [MemberData(nameof(NullishCoalescingTests))]
    public void TestParenthesizeBinaryExpressionMixingNullishCoalescing(NullishCoalescingCase tt)
    {
        output.WriteLine(tt.Title);

        var factory = new AstNodeFactory();
        var innerExpr = factory.NewBinaryExpression(
            null, /*modifiers*/
            factory.NewIdentifier("a"),
            null, /*typeNode*/
            factory.NewToken(tt.InnerOp),
            factory.NewIdentifier("b"));
        Node outerExpr;
        if (tt.Side == "left")
        {
            outerExpr = factory.NewBinaryExpression(
                null,      /*modifiers*/
                innerExpr, /*left: (a innerOp b)*/
                null,      /*typeNode*/
                factory.NewToken(tt.OuterOp),
                factory.NewIdentifier("c"));
        }
        else
        {
            outerExpr = factory.NewBinaryExpression(
                null, /*modifiers*/
                factory.NewIdentifier("a"),
                null, /*typeNode*/
                factory.NewToken(tt.OuterOp),
                innerExpr /*right: (b innerOp c)*/);
            // adjust identifiers for right side
            innerExpr.AsBinaryExpression()!.Left = factory.NewIdentifier("b");
            innerExpr.AsBinaryExpression()!.Right = factory.NewIdentifier("c");
        }

        var file = NewSyntheticFile(factory, [
            factory.NewExpressionStatement(outerExpr),
        ]);

        EmitTestUtil.CheckEmit(null, file, tt.Output);
    }

    // Go: TestOmitTrailingSemicolon。
    [Fact]
    public void TestOmitTrailingSemicolon()
    {
        var factory = NewNodeFactory(new NodeFactoryHooks());
        var methodSignature = factory.NewMethodSignatureDeclaration(
            null, /*modifiers*/
            factory.NewIdentifier("m"),
            null, /*postfixToken*/
            null, /*typeParameters*/
            factory.NewNodeList(null),
            factory.NewKeywordTypeNode(Kind.VoidKeyword));
        var file = ParseTestUtil.ParseTypeScript("interface I {}", false);

        var defaultPrinter = PrinterClass.NewPrinter(new PrinterOptions { NewLine = NewLineKind.LF }, new PrintHandlers(), null);
        Assert.Equal("m(): void;", defaultPrinter.Emit(methodSignature, file));

        var omitPrinter = PrinterClass.NewPrinter(new PrinterOptions { NewLine = NewLineKind.LF, OmitTrailingSemicolon = true }, new PrintHandlers(), null);
        Assert.Equal("m(): void", omitPrinter.Emit(methodSignature, file));

        var forFile = ParseTestUtil.ParseTypeScript("for (;;) {}", false);
        var forText = omitPrinter.EmitSourceFile(forFile);
        Assert.Equal("for (;;) { }", forText.EndsWith('\n') ? forText[..^1] : forText);
    }

    // Go 每个合成节点测试重复的 preamble（`var factory ast.NodeFactory` /
    // `factory.NewSourceFile(ast.SourceFileParseOptions{FileName: "/file.ts", Path: "/file.ts"}, "",
    // factory.NewNodeList(statements), factory.NewToken(ast.KindEndOfFile))` /
    // `parsetestutil.MarkSyntheticRecursive(file)`）抽成一个方法；每个用例的工厂调用序列与期望输出与 Go 逐行对应。
    private static SourceFile NewSyntheticFile(AstNodeFactory factory, Node[] statements)
    {
        var file = factory.NewSourceFile(
            new SourceFileParseOptions { FileName = "/file.ts", Path = new Path("/file.ts") },
            "",
            factory.NewNodeList(statements),
            factory.NewToken(Kind.EndOfFile)).AsSourceFile()!;

        ParseTestUtil.MarkSyntheticRecursive(file);
        return file;
    }
}
