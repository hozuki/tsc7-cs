// Ast 生成物 + 手写核心的行为哨兵。
//
// 为什么需要它：`Nodes.Generated.cs`/`Kind.Generated.cs`/`KindStringer.Generated.cs` 是
// tools/TypeScript.CodeGen 的产物，随时可能重新生成；生成器只保证"再跑一次结果一样"，
// 不保证"生成出来的行为对"。Go 侧 ast 的行为测试（deepclone_test.go 等）要等 parser 落地后
// 才能移植，所以这里先把"工厂/计数器/hooks、Kind 与 stringer、ForEachChild/VisitEachChild/Clone、
// SubtreeFacts（含 composite 缓存与访问器共享 helper）、ModifierList/ModifierFlags、GetNodeId"的可观察
// 行为钉住（对应一次性的 artifacts/ast-smoke 冒烟，已正式化为测试）。
using TypeScript.Ast;
using TypeScript.Core;
using Xunit;

using static TypeScript.Ast.Ast;

namespace TypeScript.Tests;

public class AstGeneratedCodeTests
{
    private static (NodeFactory Factory, List<Node> Created, int[] Updated, int[] Cloned) NewFactory()
    {
        var created = new List<Node>();
        var updated = new int[1];
        var cloned = new int[1];
        var factory = NewNodeFactory(new NodeFactoryHooks
        {
            OnCreate = created.Add,
            OnUpdate = (_, _) => updated[0]++,
            OnClone = (_, _) => cloned[0]++,
        });
        return (factory, created, updated, cloned);
    }

    [Fact]
    public void FactoryCountersAndHooks()
    {
        var (f, created, _, _) = NewFactory();
        var id = f.NewIdentifier("x");
        var idText = f.NewIdentifier("y");
        var block = f.NewBlock(f.NewNodeList([id, idText]), true);
        var ifStmt = f.NewIfStatement(f.NewIdentifier("cond"), block, null);

        Assert.Equal(Kind.Identifier, id.Kind);
        Assert.Equal("x", id.AsIdentifier()!.Text);
        Assert.Null(id.Parent);
        Assert.Null(ifStmt.Parent); // 工厂不设置 Parent（与 Go 一致）
        Assert.Equal(f.NodeCount(), created.Count);
        Assert.Equal(3, f.TextCount()); // 三个 Identifier 各带文本
        Assert.Equal(-1, id.Loc.Pos());
        Assert.Equal(-1, id.Loc.End());
        Assert.Same(block, ifStmt.AsIfStatement()!.ThenStatement);
        Assert.Null(id.AsBlock()); // 对应 Go 的 type assert 失败
        Assert.True(IsIdentifier(id));
        Assert.False(IsIdentifier(block));
    }

    [Fact]
    public void KindValuesAndStringer()
    {
        Assert.Equal(79, (int)Kind.Identifier); // 与 Go 的 kind_generated.go 数值一致（artifacts/kind-verify 全量核对过）
        Assert.Equal("KindIdentifier", Kind.Identifier.String());
        Assert.Equal("KindCount", ((Kind)351).String());
        Assert.Equal("Kind(9999)", ((Kind)9999).String()); // 越界走 Go 的 fmt 形态
        Assert.True(IsTokenKind(Kind.Identifier));
        Assert.False(IsTokenKind(Kind.SourceFile));
        Assert.True(IsKeywordKind(Kind.AbstractKeyword));
        Assert.False(IsKeywordKind(Kind.Identifier));
    }

    [Fact]
    public void ForEachChildVisitEachChildAndClone()
    {
        var (f, _, updated, cloned) = NewFactory();
        var id = f.NewIdentifier("x");
        var block = f.NewBlock(f.NewNodeList([id, f.NewIdentifier("y")]), true);
        var stmt = f.NewExpressionStatement(f.NewIfStatement(f.NewIdentifier("cond"), block, null));

        var seen = new List<string>();
        stmt.ForEachChild(n =>
        {
            seen.Add(n.Kind.String());
            return false;
        });
        Assert.Equal(["KindIfStatement"], seen); // 只访问直接子节点

        // 访问者把每个 Identifier 换成新的 Identifier（走 UpdateXxx 的新建路径）
        NodeVisitorVisit? visit = null;
        var visitor = new NodeVisitor(n => visit!(n));
        visit = n => n.Kind == Kind.Identifier ? f.NewIdentifier(n.Text() + "!") : visitor.VisitEachChild(n);
        var visited = visitor.VisitEachChild(stmt)!;

        var newId = visited.AsExpressionStatement()!.Expression!.AsIfStatement()!.Expression!.AsIdentifier()!;
        Assert.Equal("cond!", newId.Text);
        Assert.Equal("x!", visited.AsExpressionStatement()!.Expression!.AsIfStatement()!.ThenStatement!.AsBlock()!.Statements!.Nodes![0]!.AsIdentifier()!.Text);
        Assert.NotSame(stmt, visited);
        Assert.Equal("x", ((Node)id).Text()); // VisitEachChild 不修改原节点
        Assert.Equal(0, updated[0]);

        var clone = visited.Clone(f)!;
        Assert.Equal(visited.Kind, clone.Kind);
        Assert.NotSame(visited, clone);
        Assert.Equal("cond!", clone.AsExpressionStatement()!.Expression!.AsIfStatement()!.Expression!.AsIdentifier()!.Text);
        Assert.True(cloned[0] >= 1); // OnClone 钩子触发
        Assert.NotNull(f.NewIdentifier("z").AsIdentifier()!.Clone(f));
    }

    [Fact]
    public void SubtreeFactsAggregationAndCaching()
    {
        var (f, _, _, _) = NewFactory();
        var id = f.NewIdentifier("x");
        Assert.Equal(SubtreeFacts.ContainsIdentifier, id.SubtreeFacts);

        var typeRef = f.NewTypeReferenceNode(f.NewIdentifier("T"), null);
        Assert.Equal(SubtreeFacts.ContainsTypeScript, typeRef.SubtreeFacts); // TypeSyntaxBase 的行为
        var cached = typeRef.SubtreeFacts;
        Assert.Equal(cached, typeRef.SubtreeFacts);
        Assert.Equal(SubtreeFacts.None, cached & SubtreeFacts.Computed); // 不把缓存标记位暴露出去

        var block = f.NewBlock(f.NewNodeList([f.NewExpressionStatement(id), typeRef]), false);
        Assert.Equal(
            SubtreeFacts.ContainsIdentifier | SubtreeFacts.ContainsTypeScript,
            block.SubtreeFacts & (SubtreeFacts.ContainsIdentifier | SubtreeFacts.ContainsTypeScript));

        // Go 的 AccessorDeclarationBase.computeSubtreeFacts（ast.go:1950-1963）：无 body 的访问器声明算 ContainsTypeScript
        var withBody = f.NewGetAccessorDeclaration(null, f.NewIdentifier("p"), null, f.NewNodeList([]), null, null, f.NewBlock(f.NewNodeList([]), false));
        Assert.Equal(SubtreeFacts.ContainsIdentifier, withBody.SubtreeFacts);
        var noBody = f.NewGetAccessorDeclaration(null, f.NewIdentifier("p"), null, f.NewNodeList([]), null, null, null);
        Assert.Equal(SubtreeFacts.ContainsTypeScript, noBody.SubtreeFacts);
    }

    [Fact]
    public void ModifierListAndFlags()
    {
        var (f, _, _, _) = NewFactory();
        var modifiers = f.NewModifierList([f.NewModifier(Kind.ExportKeyword), f.NewModifier(Kind.AsyncKeyword)]);
        Assert.Equal(ModifierFlags.Export | ModifierFlags.Async, modifiers.ModifierFlags);

        var varStmt = f.NewVariableStatement(modifiers, f.NewVariableDeclarationList(f.NewNodeList([]), NodeFlags.Const));
        Assert.Equal(ModifierFlags.Export | ModifierFlags.Async, varStmt.ModifierFlags);
        Assert.True(HasSyntacticModifier(varStmt, ModifierFlags.Export));
        Assert.NotEqual(NodeFlags.None, varStmt.AsVariableStatement()!.DeclarationList!.Flags & NodeFlags.Const);
        Assert.Equal(modifiers.ModifierFlags, modifiers.Clone(f).ModifierFlags);
    }

    [Fact]
    public void NodeIdsSymbolsAndSmallTypes()
    {
        var (f, _, _, _) = NewFactory();
        var id = f.NewIdentifier("x");
        var other = f.NewIdentifier("y");

        var nodeId = GetNodeId(id);
        Assert.Equal(nodeId, GetNodeId(id));
        Assert.NotEqual(nodeId, GetNodeId(other));

        var symbol = new Symbol { Name = "s" };
        Assert.Equal(GetSymbolId(symbol), GetSymbolId(symbol));
        Assert.False(symbol.IsExternalModule());
        Assert.Equal(SymbolFlags.None, symbol.CombinedLocalAndExportSymbolFlags());

        var table = new SymbolTable { ["x"] = symbol };
        Assert.Single(table);
        Assert.Equal(Kind.JSDoc, new CommentRange { Kind = Kind.JSDoc }.Kind);
        Assert.True(new PragmaSpecification { Kind = PragmaKind.TripleSlashXML }.IsTripleSlash());
        Assert.NotSame(NewSourceFileDataKey<int>(), NewSourceFileDataKey<int>());
    }

    [Fact]
    public void KindAliasFactoriesProduceAliasKind()
    {
        // 回归哨兵（对应工具链缺陷）：生成器曾让别名工厂发射**基类** kind，于是
        // UpdateXxx 里 `case Kind.JSTypeAliasDeclaration` 永远不可达、IsJSTypeAliasDeclaration 恒 false。
        // 期望值来自 Go 的 ast_generated.go（NewJSTypeAliasDeclaration → newNode(KindJSTypeAliasDeclaration, …)、
        // NewJSImportDeclaration → newNode(KindJSImportDeclaration, …)）；全量 186 个工厂的 kind
        // 已由 artifacts/ast-diff/factory-kinds.mjs 与 Go 机械核对（别名工厂 33 个）。
        var (f, _, _, _) = NewFactory();

        var jsTypeAlias = f.NewJSTypeAliasDeclaration(null, f.NewIdentifier("Foo"), null, null);
        Assert.Equal(Kind.JSTypeAliasDeclaration, jsTypeAlias.Kind);
        Assert.True(IsJSTypeAliasDeclaration(jsTypeAlias));
        // 普通工厂给基类 kind
        Assert.Equal(Kind.TypeAliasDeclaration, f.NewTypeAliasDeclaration(null, f.NewIdentifier("Bar"), null, null).Kind);

        var jsImport = f.NewJSImportDeclaration(null, null, f.NewStringLiteral("x", TokenFlags.None), null);
        Assert.Equal(Kind.JSImportDeclaration, jsImport.Kind);
        Assert.Equal(Kind.ImportDeclaration, f.NewImportDeclaration(null, null, f.NewStringLiteral("y", TokenFlags.None), null).Kind);

        // Update 的 kind 分派必须与工厂一致（否则 update 会退化成 default 分支抛异常）
        Assert.Equal(Kind.JSTypeAliasDeclaration, f.UpdateTypeAliasDeclaration((TypeAliasDeclaration)jsTypeAlias, null, f.NewIdentifier("Baz"), null, null).Kind);
    }
}
