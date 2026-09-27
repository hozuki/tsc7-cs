// ported from: internal/ast/ast.go（SourceFile，ast.go:2446-3086）
//
// ⚠ 本轮只落地能支撑已移植代码的最小部分（见下），其余字段/方法**推迟到下一轮**（Go 源文件:行的名单
// 见文件末尾），因为 SourceFile 的完整实现依赖尚未移植的 package：
//   * spanmap.SpanMap（C# 侧还没有这个 package）
//   * diagnostics（M3 的另一个生成器产出，尚未落地）
//   * parser 的懒 JSDoc 解析（ast.go 的 parseJSDocForNode 回调）
//   * core.Pattern / core.TextPos / tspath.Path 已有，可以直接用，但依赖上面几项的方法仍无法完成
//
// Go 的形状（ast.go:2446）：
//
//	type SourceFile struct {
//	    NodeBase; DeclarationBase; LocalsContainerBase; CompositeBase
//	    Statements *StatementList; EndOfFileToken *EndOfFile; ...
//	}
//
// 与 Go 的差异：Go 的未导出字段/方法按 C# 习惯命名（`_camelCase` / PascalCase）；`SubtreeFacts`/`ModifierFlags`
// 是属性（见 Node.cs 文件头）。
using TypeScript.Core;

namespace TypeScript.Ast;

public sealed partial class SourceFile : Node, IDeclarationBase, ILocalsContainerBase, ICompositeBase
{
    // Go: Statements *StatementList（非 optional）
    public new NodeList? Statements;

    // Go: EndOfFileToken *EndOfFile
    public Node? EndOfFileToken;

    // Go: DeclarationBase.Symbol（goOnly）
    public new Symbol? Symbol;

    Symbol? IDeclarationBase.Symbol
    {
        get => Symbol;
        set => Symbol = value;
    }

    // Go: LocalsContainerBase.Locals / NextContainer（goOnly）
    public new SymbolTable? Locals;
    public Node? NextContainer;

    SymbolTable? ILocalsContainerBase.Locals
    {
        get => Locals;
        set => Locals = value;
    }

    Node? ILocalsContainerBase.NextContainer
    {
        get => NextContainer;
        set => NextContainer = value;
    }

    // Go: CompositeBase.facts atomic.Uint32（缓存 SubtreeFacts；见 Node.SubtreeFacts）
    internal int _facts;

    int ICompositeBase.Facts
    {
        get => Volatile.Read(ref _facts);
        set => Volatile.Write(ref _facts, value);
    }

    // Go: DeclarationBase.DeclarationData() / LocalsContainerBase.LocalsContainerData()
    public override IDeclarationBase? DeclarationData() => this;

    public override ILocalsContainerBase? LocalsContainerData() => this;

    // Go: SourceFile.ScriptKind / IsDeclarationFile / ExternalModuleIndicator
    //     （parseoptions.go 的 SetExternalModuleIndicator 与 getExternalModuleIndicator 用到的字段）
    public ScriptKind ScriptKind;
    public bool IsDeclarationFile;
    public Node? ExternalModuleIndicator;

    // Go: func (node *SourceFile) computeSubtreeFacts()（ast.go:2821-2823）—— 推迟到下一轮（见文件末尾名单）。
}

// 推迟到下一轮的 Go 符号（SourceFile 本体，ast.go）：
//   2446-2525  SourceFile 的其余字段（FileName/Path/Text/... 、spanmap/diagnostics/jsdocCache/dataMu/data 等）
//   2526-2538  func (f *NodeFactory) NewSourceFile
//   2539-2638  ParseOptions/Text/OriginalText/OriginalFileName/SpanMap/ContentMapper/...
//   2639-2700  ContentMapper* / SupplementalSourceFiles / CanonicalSourceFile / HasIdentifier
//   2701-2744  FileName/Path/Imports/Diagnostics/…JSDoc 缓存
//   2745-2820  resolveJSDoc / BindDiagnostics / SetBindDiagnostics / ForEachChild / VisitEachChild / IsJS /
//              copyFrom / Clone
//   2825-2833  func (f *NodeFactory) UpdateSourceFile
//   2834-3103  ECMALineMap / GetNameTable / IsBound / GetPositionMap / BindOnce / GetOrCreateToken /
//              createToken / GetDeclarationMap / computeDeclarationMap / GetDeclarationName
//   2404-2430  func (file *SourceFile) GetOrComputeData[T] / getDataCell[T]
// （这些方法的 Go 行号与理由已记入本轮报告。）
