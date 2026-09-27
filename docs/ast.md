# Ast 包的 C# 表示（M3 契约）

Go 侧：`internal/ast/ast.go`（手写核心，3189 行）、`ast_generated.go`（由 `tools/scripts/tsc/ast.json` + `generate-go-ast.ts` 生成，10058 行）、`kind_generated.go`、各类 flags/工具文件。
C# 侧：`src/TypeScript.Compiler/Ast/`，生成器在 `tools/TypeScript.CodeGen`。

## 1. Go 的形状（作为事实基础）

```go
type Node struct {            // 手写：节点头
    Kind Kind; Flags NodeFlags; Loc core.TextRange
    id atomic.Uint64; Parent *Node
    data nodeData             // 接口：20 个方法（AsNode/ForEachChild/VisitEachChild/Clone/
}                             //   Name/Modifiers/setModifiers/*Data()/SubtreeFacts…）

type NodeDefault struct { Node }     // 默认实现：全部返回 nil/false
type NodeBase struct { NodeDefault } // 生成的基础里最底层的一个（ast.json bases: NodeBase）

type Identifier struct {      // 生成：节点数据（字段照 ast.json）
    PrimaryExpressionBase     // 多个内嵌基础（Go 的 mixin）
    FlowNodeBase
    Text string
}
func (n *Node) AsIdentifier() *Identifier { … n.data.(*Identifier) }   // 生成
func IsIdentifier(node *Node) bool { return node.Kind == KindIdentifier }
```

要点：Go 把节点头**内嵌在数据结构体里**（`data.AsNode()` 返回内嵌头的指针），所以一个节点一次分配、`*Node` 就是身份的载体。节点专有字段通过 `node.AsXxx().Field` 访问，基础（mixin）字段通过 `node.XxxData()` 拿到内嵌基础结构体后访问其字段。

## 2. C# 表示（决定）

- **一个节点一个对象**：`Node` 是抽象基类（Kind/Flags/Loc/Parent/Id），节点类型是 `sealed class Identifier : Node, IFlowNodeBase, IDeclarationBase`。**不做** Go 的 `nodeData` 接口那一层——Go 用它是因为结构体内嵌只能有默认方法集；C# 直接用虚方法/继承表达，语义等价（`n.data.Name()` 的接口调用 ↔ C# 的虚调用）。
- **访问器**：Go 的 `nodeData` 方法集落成 `Node` 上的 `virtual` 方法，默认值照 `NodeDefault`（`Name() => null`、`Modifiers() => null`、`ForEachChild(...) => false`、`Clone(...) => null`、`SubtreeFacts()`…）。生成的节点类**只覆写与自己相关的**。
- **mixin 基础**：`IFlowNodeBase`/`IDeclarationBase`/`IExportableBase`/`ILocalsContainerBase`/`IFunctionLikeBase`/`IClassLikeBase`/`IBodyBase`/`ILiteralLikeNodeBase`/`ITemplateLiteralLikeNodeBase` 等接口（名字 = Go 基础名加 `I`），供 `is` 判断与 `node.XxxData()` 使用；**没有字段的基础（brand 标记、零尺寸）只做接口**。
- **`node.XxxData()`**：返回 `IXxxBase?`（= 该节点自身，若不是该基础则 null），对应 Go 返回内嵌基础结构体指针（Go 里也是 nil / 非 nil）。调用点 `d := n.DeclarationData(); d.Foo` → C# `var d = n.DeclarationData(); if (d is not null) { … d.Foo … }`。
- **字段命名（用户要求）**：
  * Go **导出**字段（`Text`、`ElementType`、`TypeParameters`…）→ C# 同名 public 成员（名字照抄）。
  * Go **未导出**字段（`name`、`modifiers`、`facts`…）→ C# `_camelCase` 实例字段（`_name`、`_modifiers`、`_facts`），可见性按需要 `private`/`internal`。**不要**用 `Name_`/尾下划线这种"照抄 Go 的避让写法"，TSGO 的 ast 里也没有这种名字（`name` 是未导出字段，公开访问走 `Name()`）。
  * Go 未导出的方法（`setModifiers`、`computeSubtreeFacts`、`newNode`…）→ PascalCase（`SetModifiers`、`ComputeSubtreeFacts`、`NewNode`）。
  * 节点专有数据与基础数据都用 public 字段（不是属性）：调用点可以逐字移植（`d.Symbol`、`n.AsIdentifier().Text`），并且 C# 里仍有 `ref node.Field` 这种用法可写。基础字段要满足接口时用**显式接口实现**转发：
    ```csharp
    public sealed class Identifier : Node, IFlowNodeBase {
        public FlowNode? Flow;                                  // Go: FlowNodeBase.Flow（导出字段）
        FlowNode? IFlowNodeBase.Flow { get => Flow; set => Flow = value; }
    }
    ```
- **`As` 转换**：Go 在 `*Node` 上生成 `func (n *Node) AsIdentifier() *Identifier`（type assert on `n.data`）→ C# 在 `Node` 上生成 `public Identifier? AsIdentifier() => this as Identifier;`（kind 不匹配时 null，与 Go 一致）。
- **访问器名与类型名相同的特例**：`SubtreeFacts`、`ModifierFlags` 在 Go 里既是类型名又是 `nodeData` 的访问器方法名；C# 的 "Color Color" 规则只覆盖常量/字段/属性/局部/参数，**方法会遮蔽类型名**（`SubtreeFacts.None` 会 CS0119）。所以这两个访问器在 C# 里写成**属性**（`public virtual SubtreeFacts SubtreeFacts { get; }`、`public virtual ModifierFlags ModifierFlags { get; }`），调用点从 Go 的 `x.SubtreeFacts()` 变成 `x.SubtreeFacts`。其余访问器保持方法形态。
- **`Is` 判断**：`public static bool IsIdentifier(Node node) => node.Kind == Kind.Identifier;`（放静态类 `Ast`，对应 Go 的包级函数）。
- **Kind**：由 ast.json 的 `kinds` 生成 `enum Kind`（**数值与 Go 一致**）+ `ToString()`（Go 的 `kind_stringer_generated.go` 落成扩展方法 `String(this Kind)`，见 docs/architecture.md §3.3）+ `<Xxx>Node = Node` 之类的别名（Go 的 `type XxxNode = Node` 在 C# 里就是 `Node`，不生成别名，调用点直接用 `Node`）。
- **NodeFactory**：与 Go 同名的方法（`NewIdentifier(text)`、`UpdateIdentifier(node, …)`），内部 `new Identifier { … }` 后登记到 `NodeFactory.NewNode(kind, node)`；`NodeFactory` 的 arena 字段按 docs §3.2 去掉（AST 节点是 class）。
- **`ForEachChild`/`VisitEachChild`/`Clone`/`SubtreeFacts`**：由生成器按 ast.json 的成员表生成（与 generate-go-ast.ts 的判定逐条对应：哪些成员是子节点、哪些参与 update、哪些影响 subtree facts）。
- **身份语义**：Go 的 `*Node` 指针相等 ↔ C# 引用相等（`ReferenceEquals`），**不要**给 Node 覆写 `Equals`/`GetHashCode`。
- **M3 实测补充（手写核心）**：
  * `SubtreeFacts`/`ModifierFlags`/`Symbol` 落成 `Node` 上的**属性**（名字与返回类型名相同；C# 里方法会遮蔽类型名，属性不会——Color Color 规则），调用点由 Go 的 `x.SubtreeFacts()` 改成 `x.SubtreeFacts`。其余访问器保持 Go 的方法形态（`Name()`、`Modifiers()`、`FlowNodeData()`…）。
  * Go 的 `MutableNode`（`type MutableNode Node` + `AsMutable()`）在 C# 里没有指针重解释：`AsMutable()` 返回 `this`，`SetModifiers`/`SetExpression`/`SetType`/`SetInitializer` 落在 `Node` 上。
  * `nodeData` 那层接口不生成；`NodeDefault` 的默认值就是 `Node` 上的 `virtual` 实现（`Clone`/`Name`/`Modifiers`/`*Data()` 默认 null，`ForEachChild` 默认 false，`SubtreeFacts` 是 `ComputeSubtreeFacts()`，composite 节点的缓存由 `Node.SubtreeFacts` 统一实现，用 `this is ICompositeBase` 判分支）。
  * composite 的 `facts` 缓存：Go 的 `atomic.Uint32` → `internal int _facts` + `ICompositeBase.Facts` 属性里用 `Volatile.Read/Write`。
  * `NodeFactory` 不再有 arena（节点是 class）：`NewNodeList`/`NewModifierList` 直接 `new`；`ReleaseArenas()` 只重置计数器。
  * `Kind` 枚举数值与 `kind_generated.go` 一致（`KindIdentifier == 79` 等，已用 Go stringer 的 `_ = x[KindXxx-N]` 断言机械核对）。
  * 本轮推迟（Go 文件:行见报告）：`SourceFile` 的完整实现（字段/工厂/ECMALineMap/GetOrCreateToken/GetDeclarationMap/…）、`Node.JSDoc`/`EagerJSDoc`、`GetDeclarationFromName`/`declarationIsWriteAccess`/`accessKind`、`ImportAttributesNode.GetResolutionModeOverride`、`BinaryExpression` 里依赖 `ContainsObjectRestOrSpread` 的那一段、`utilities.go` 的其余部分、`parseoptions.go` 的 `SetExternalModuleIndicator` 系列。

## 3. 生成器（tools/TypeScript.CodeGen）

- 输入：`<TSGO_ROOT>/tools/scripts/tsc/ast.json`（语言中立的 AST 模型）。**决策依据**必须来自 `generate-go-ast.ts`（1107 行）：逐条搬运它的判定（哪些 base 是零尺寸、mixin 采集、update 工厂的比较列表、ForEachChild 的子节点列表、VisitEachChild、Clone、subtree facts、kind 别名守卫、name accessor），只把**输出语法**从 Go 换成 C#。
- 输出：`src/TypeScript.Compiler/Ast/Nodes.Generated.cs`、`Kind.Generated.cs`、`KindStringer.Generated.cs`（文件名/拆分可调整，但必须集中在 `*.Generated.cs` 并写清输入与命令）。
- CLI：沿用 Unicode 生成器的 `--out <dir>`（AST 输出目录，缺省 `src/TypeScript.Compiler/Ast`）；Unicode 表另有 `--unicode-out <dir>`（缺省 `src/TypeScript.Compiler/StringUtil`）。
- 幂等：内容相同则不重写文件（与 Unicode 生成器一致）。
- 验收：生成物在 AOT 分析器下 0 警告 0 错误；节点类型数量、每个节点的字段名、工厂方法签名与 Go 的 `ast_generated.go` 做**机械 diff**（脚本提取两边名字/字段列表比较，0 处不一致）。

### 3.1 生成器里逐条落地的形状决定（M3 实测）

| 决定 | 理由 |
| --- | --- |
| 节点类 `sealed partial class X : Node, I<直接基础>…`，基础字段**平铺**进类，导出字段 `public`、未导出字段 `internal _camelCase` | C# 无多重继承；`XxxData()` 走接口，字段要能被同程序集代码读到 |
| 基础（mixin）→ `public interface I<基础名>`，导出字段是 `{ get; set; }` 属性，类里**显式接口实现**转发；`ICompositeBase` 是 `public` 接口但成员 `internal int Facts`（Go 的 `facts` 未导出、只被 Node 的缓存读） | Go 的内嵌结构体 ↔ 接口 + 转发；`facts` 不进公开 API |
| 与 `Node` 访问器同名的字段加 `new`（31 个名字：`Text`/`Type`/`Expression`/`Symbol`/`Parameters`/…） | Go 里字段同名同样遮蔽方法（`id.Text` 取字段）；C# 不写 `new` 会 CS0108 |
| `As*()` 生成在 `Node` 上，用 `this as X`；`Is*()`/Kind 别名守卫/`String(this Kind)` 生成在静态类 `Ast` | 对应 Go 的 `(*Node).AsXxx` / 包级 `IsXxx` |
| 多 kind 节点（Kind 成员是联合或类型参数）逐 kind 生成 `IsXxx`；`kind` 数组（TypeAliasDeclaration/ImportDeclaration）另生 `Is<别名>` 与 `New<别名>` | 与 `generate-go-ast.ts` 的 `isMultiKind`/`kindAliases` 判定一致 |
| `Kind` 枚举：成员名去 `Kind` 前缀、**显式写数值**、`Count` 与全部 marker 都是成员；kind 别名不生成成员（守卫按展开后的具体 kind 生成 switch/range） | 数值与 `kind_generated.go` 逐条一致；C# 没有类型别名 |
| `KindStringer.Generated.cs`：`private static readonly string[] _kindNames`（含 `"KindCount"`）+ `String(this Kind)`；越界返回 `"Kind(<n>)"` | 与 Go stringer 的文本逐字一致（`_Kind_name`/`_Kind_index` 的等价展开）；数组查表零分配 |
| 生成文件带 `#nullable enable` | `<auto-generated>` 头会让 Roslyn 关掉可空注解上下文（否则每个 `T?` 都是 CS8669） |
| 工厂 `NewXxx(...)`：`var data = new Xxx();` + 逐字段赋值 + `_textCount++` + `NewNode(kindExpression, data)`；NodeFlags 成员在 `NewNode` 之后 `node.Flags = x` / `|= x & mask` | 与 Go 生成物的语句顺序一致（`textCount` 先于 `newNode`、NodeFlags 后写） |
| `UpdateXxx` 的比较用 `!Ast.Same(...)`（raw 列表）/ `!=`（其余），无子节点的叶子不生成 | 照抄 `generateUpdateFactory` |
| `VisitEachChild` 调 `v.VisitXxxWithHooks(...)`；raw 列表先 `SameMap(field, n => v.VisitNodeWithHooks(n)!)` | Go 的 `v.visitXxx`（会走 hooks）在 C# 里必须加 `WithHooks` 后缀（见 Visitor.cs 文件头） |
| `panic(...)` → `throw new DebugFailureException(...)` | 让编译器接受控制流（`Debug.Fail` 只是它的包装） |
| 生成器里的 kind 别名先记录原始成员、加载完所有别名后**再统一展开** | ast.json 里边可以引用后面才定义的别名（`AssignmentOperatorOrHigher` 等） |

