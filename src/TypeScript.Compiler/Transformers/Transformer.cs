// ported from: internal/transformers/transformer.go（41 行，全部）
//
// ─────────────────────────────────────────────────────────────────────────────
// 本文件是整个 transformers 包（T1–T9）的**形状契约**，派生变换器一律按此写：
//
// 1) Go 的 `type Transformer struct` → **C# `class Transformer`（非 abstract）**。
//    Go 用 `*Transformer` 做身份（各变换器结构体**嵌入**它，由外层提供 `visit`）；C# 没有嵌入 →
//    用**继承**表达（`XxxTransformer : Transformer`），`NewTransformer` 返回 `this`
//    （与 Go 返回内嵌字段指针 `&tx.Transformer` 等价：运行时是同一个对象，`tx.visit` 仍派发到派生类）。
//
// 2) TSGO 里**没有** `TransformContext` / `TransformationResult` 这两个类型（TS/Strada 的那套职责在 Go 移植时
//    被拆进了 `printer.EmitContext`：lexical/variable environment、hoist、emit helper、original/source-map/comment
//    记录、`ParseNode`…全部在那里）。因此本包**不造**这两个类型——变换器通过 `tx.EmitContext()` 取得这些能力。
//    对应关系（Go 调用点 → C#）：
//      transformContext.hoistVariableDeclaration(name)      → EmitContext().AddVariableDeclaration(name)
//      transformContext.endLexicalEnvironment()              → EmitContext().EndLexicalEnvironment()
//      transformContext.addEmitHelper(helper)                → EmitContext().AddEmitHelper(node, helper)
//      transformContext.readEmitHelpers()                    → EmitContext().ReadEmitHelpers()
//
// 3) `Transformer` 的成员保持 **方法**形态（与 Go 导出方法逐个对应）：
//    `EmitContext()` / `Visitor()` / `Factory()` / `TransformSourceFile(file)` / `NewTransformer(visit, emitContext)`。
//    `visit` 复用 Ast 已有的具名委托 `NodeVisitorVisit`（`Node? (Node)` = Go 的 `func(node *ast.Node) *ast.Node`）。
//
// 4) 变换器工厂函数命名（Go 的 `func NewXxxTransformer(ctx *TransformOptions) *Transformer`）：
//    `TypeScript.Transformers.<Xxx>Transformer.NewXxxTransformer(opt)` 保持 Go 的签名与返回值可空性，
//    或（Go 里就是包级函数时）放进**与包同名的静态类** `Transformers` / `Inliners` 等。派生类模板：
//
//        public sealed class XxxTransformer : Transformer
//        {
//            public static Transformer? NewXxxTransformer(TransformOptions opt)
//            {
//                var tx = new XxxTransformer { /* … */ };
//                return tx.NewTransformer(tx.Visit, opt.Context);   // Go: tx.NewTransformer(tx.visit, emitContext)
//            }
//
//            private Node? Visit(Node node) { /* … */ }             // Go: func (tx *XxxTransformer) visit(node *ast.Node) *ast.Node
//        }
//
// 5) Go 未导出字段 → `_camelCase`（跨类/跨 partial 读用 `internal`）；Go 导出字段 → 原名 PascalCase。
//
// 与 Go 的偏离（C# 语言特性）：
// - 无（`Transformer` 本身是逐行对应；`go` 的 nil 接收者不可分派，本类方法不处理 nil 接收者）。
// - 注意（C# 命名解析，不是 API 偏离）：本类的**方法** `EmitContext()` 会遮蔽**类型**名 `EmitContext`
//   （Color Color 规则不覆盖方法）；并且命名空间 `TypeScript.Printer` 会遮蔽包级静态类 `Printer`
//   （外层命名空间的成员优先于 using 导入的类型）。所以 `printer.NewEmitContext()` 要写全限定
//   `TypeScript.Printer.Printer.NewEmitContext()`。
using TypeScript.Ast;
using TypeScript.Printer;
using NodeFactory = TypeScript.Printer.NodeFactory; // 消歧：Ast 与 Printer 各有一个 NodeFactory

namespace TypeScript.Transformers;

/// <summary>
/// Go 的 <c>type Transformer struct { emitContext *printer.EmitContext; factory *printer.NodeFactory; visitor *ast.NodeVisitor }</c>
/// ＋ 其 5 个方法。<br/>
/// 用法（与 Go 相同）：派生类在构造末尾调用 <see cref="NewTransformer"/>，把 |this| 作为变换器返回。
/// </summary>
public class Transformer
{
    // Go 未导出字段 → _camelCase（internal：Chain/ExtractModifiers 等包级函数与 ChainedTransformer 要读）。
    internal EmitContext? _emitContext;
    internal NodeFactory? _factory;
    internal NodeVisitor? _visitor;

    /// <summary>Go: <c>func (tx *Transformer) NewTransformer(visit func(node *ast.Node) *ast.Node, emitContext *printer.EmitContext) *Transformer</c>。</summary>
    public Transformer NewTransformer(NodeVisitorVisit visit, EmitContext? emitContext)
    {
        if (_emitContext is not null)
        {
            // Go: panic("Transformer already initialized")
            throw new DebugFailureException("Transformer already initialized");
        }
        // Go: if emitContext == nil { emitContext = printer.NewEmitContext() }
        // 注意：`NewEmitContext` 是 **静态类 `Printer`（= Go 的 package printer）** 的成员，不是 `EmitContext` 的；
        // 而命名空间 `TypeScript.Printer` 会遮蔽类 `Printer`（外层命名空间优先于 using 导入的类型）→ 必须全限定。
        emitContext ??= TypeScript.Printer.Printer.NewEmitContext();
        _emitContext = emitContext;
        _factory = emitContext.Factory;
        _visitor = emitContext.NewNodeVisitor(visit);
        return this;
    }

    /// <summary>Go: <c>func (tx *Transformer) EmitContext() *printer.EmitContext</c>。</summary>
    public EmitContext EmitContext() => _emitContext!;

    /// <summary>Go: <c>func (tx *Transformer) Visitor() *ast.NodeVisitor</c>。</summary>
    public NodeVisitor Visitor() => _visitor!;

    /// <summary>Go: <c>func (tx *Transformer) Factory() *printer.NodeFactory</c>。</summary>
    public NodeFactory Factory() => _factory!;

    /// <summary>Go: <c>func (tx *Transformer) TransformSourceFile(file *ast.SourceFile) *ast.SourceFile</c>。</summary>
    public SourceFile TransformSourceFile(SourceFile file) => Visitor().VisitSourceFile(file);
}
