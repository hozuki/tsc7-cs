// C# 侧新增：本轮移植的 SourceFile / access-kind / SpanMap↔Diagnostic 行为在 Go 侧**没有**能直接跑的单元测试
// （Go 里唯一覆盖 SourceFile 手写部分的是 deepclone_test.go，它依赖 parser，见报告），所以这里给"可观察契约"补
// 最小哨兵。每条断言都能对回 Go 的同名实现（注释给出 Go 依据），不测转发/拷贝/只读透传：
//   * ECMALineMap：行首表内容 + 结果被缓存（Go 的 ecmaLineMapMu 双重检查）；
//   * GetOrCreateToken：缓存身份、创建出的令牌文本、kind 不一致与 Reparsed 父节点的拒绝；
//   * GetNameTable：重名 → -1、唯一名 → 位置、结果被缓存；
//   * Diagnostic：经 SourceFile.SpanMap 的 alias 段把诊断参数替换回原文（SpanMap + SourceFile + Diagnostic 端到端）；
//   * accessKind 系列：`x = 1` / `x++` / `x + x` 的读/写/读写判定 + GetDeclarationFromName 的正例。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using Xunit;

using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;

namespace TypeScript.Tests;

public class AstSourceFileTests
{
    private static SourceFile CreateSourceFile(NodeFactory factory, string text, NodeList? statements = null) =>
        factory.NewSourceFile(
            new SourceFileParseOptions { FileName = "/src/file.ts", Path = new TSPath.Path("/src/file.ts") },
            text,
            statements,
            null).AsSourceFile()!;

    [Fact]
    public void ECMALineMapComputesLineStartsAndCaches()
    {
        var factory = NewNodeFactory(new NodeFactoryHooks());
        var file = CreateSourceFile(factory, "a\nb\r\nc");

        // Go: core.ComputeECMALineStarts("a\nb\r\nc") -> [0, 2, 5]（\n 与 \r\n 各算一个换行）。
        var lineMap = file.ECMALineMap();
        Assert.Equal([0, 2, 5], Array.ConvertAll(lineMap, static pos => pos.Value));

        // Go: 第二次调用直接从缓存返回同一个切片。
        Assert.Same(lineMap, file.ECMALineMap());
    }

    [Fact]
    public void GetOrCreateTokenCachesCreatesAndValidates()
    {
        var factory = NewNodeFactory(new NodeFactoryHooks());
        var file = CreateSourceFile(factory, "identifier");
        var parent = factory.NewIdentifier("parent");

        var token = file.GetOrCreateToken(Kind.Identifier, 0, 10, parent, TokenFlags.None);
        Assert.Equal(Kind.Identifier, token.Kind);
        Assert.Equal("identifier", token.Text());
        Assert.Equal(0, token.Pos());
        Assert.Equal(10, token.End());
        Assert.Same(parent, token.Parent);

        // 同一 (parent, loc) 再取一次拿到同一个令牌（Go 的 tokenCache）。
        Assert.Same(token, file.GetOrCreateToken(Kind.Identifier, 0, 10, parent, TokenFlags.None));

        // Go: 缓存命中但 kind 不一致 → panic。
        Assert.Throws<DebugFailureException>(() => file.GetOrCreateToken(Kind.StringLiteral, 0, 10, parent, TokenFlags.None));

        // Go: 父节点是 Reparsed 时禁止从它造令牌 → panic。
        var reparsed = factory.NewIdentifier("reparsed");
        reparsed.Flags |= NodeFlags.Reparsed;
        Assert.Throws<DebugFailureException>(() => file.GetOrCreateToken(Kind.Identifier, 0, 10, reparsed, TokenFlags.None));
    }

    [Fact]
    public void GetNameTableMarksDuplicateNames()
    {
        var factory = NewNodeFactory(new NodeFactoryHooks());
        var first = factory.NewIdentifier("a");
        first.Loc = NewTextRange(0, 1);
        var second = factory.NewIdentifier("a");
        second.Loc = NewTextRange(4, 5);
        var unique = factory.NewIdentifier("b");
        unique.Loc = NewTextRange(8, 9);
        var file = CreateSourceFile(factory, "a = a; b", factory.NewNodeList([first, second, unique]));

        var nameTable = file.GetNameTable();

        // Go: 同名出现多次时值是 -1，唯一名是节点位置。
        Assert.Equal(-1, nameTable["a"]);
        Assert.Equal(8, nameTable["b"]);

        // Go: nameTableOnce 只算一次。
        Assert.Same(nameTable, file.GetNameTable());
    }

    [Fact]
    public void DiagnosticAliasSubstitutionUsesOriginalText()
    {
        var factory = NewNodeFactory(new NodeFactoryHooks());
        var file = CreateSourceFile(factory, "const renamed = 1;");
        file.SetContentMapperInfo(new ContentMapperSourceFileInfo
        {
            ContentMapper = "test-mapper",
            OriginalText = "const original = 1;",
            SpanMap = TypeScript.SpanMap.SpanMap.New(
            [
                new TypeScript.SpanMap.Segment
                {
                    VirtualStart = 6,
                    VirtualEnd = 13,
                    OriginalStart = 6,
                    OriginalEnd = 14,
                    Kind = TypeScript.SpanMap.Kind.Alias,
                    Features = TypeScript.SpanMap.Feature.All,
                },
            ]),
        });

        Assert.Equal("const renamed = 1;", file.Text());
        Assert.Equal("const original = 1;", file.OriginalText());

        // Go: 诊断正好覆盖整个 alias 段时，展示用的参数替换成原文；存储的参数不变。
        var named = NewDiagnostic(file, NewTextRange(6, 13), Cannot_find_name_0, "renamed");
        Assert.Contains("original", named.ToString());
        Assert.DoesNotContain("renamed", named.ToString());
        Assert.Equal(["renamed"], named.MessageArgs() ?? []);

        // Go: 只部分覆盖 alias 段不算（AliasForVirtualSpan 要求完整覆盖）。
        var partial = NewDiagnostic(file, NewTextRange(6, 12), Cannot_find_name_0, "renamed");
        Assert.Contains("renamed", partial.ToString());

        // Go: 外部来源（source != ""）的诊断不做替换。
        var external = NewExternalDiagnostic(file, NewTextRange(6, 13), "vue", Category.Error, 0, "renamed").ToString();
        Assert.Contains("renamed", external);
    }

    [Fact]
    public void AccessKindClassifiesReadsAndWrites()
    {
        var factory = NewNodeFactory(new NodeFactoryHooks());

        // `x = 1` —— 左操作数是纯写。
        var assigned = factory.NewIdentifier("x");
        var assignment = factory.NewBinaryExpression(null, assigned, null, factory.NewToken(Kind.EqualsToken), factory.NewNumericLiteral("1", TokenFlags.None));
        assigned.Parent = assignment;
        Assert.True(IsWriteAccess(assigned));
        Assert.True(IsWriteOnlyAccess(assigned));
        Assert.True(IsWriteAccessForReference(assigned));

        // `++y` —— 读且写。
        var incremented = factory.NewIdentifier("y");
        var prefix = factory.NewPrefixUnaryExpression(Kind.PlusPlusToken, incremented);
        incremented.Parent = prefix;
        Assert.True(IsWriteAccess(incremented));
        Assert.False(IsWriteOnlyAccess(incremented));

        // `z + z` —— 纯读。
        var read = factory.NewIdentifier("z");
        var addition = factory.NewBinaryExpression(null, read, null, factory.NewToken(Kind.PlusToken), factory.NewIdentifier("z"));
        read.Parent = addition;
        Assert.False(IsWriteAccess(read));
        Assert.False(IsWriteAccessForReference(read));

        // GetDeclarationFromName：`var v` 的 name 指向声明本身。
        var declaredName = factory.NewIdentifier("v");
        var declaration = factory.NewVariableDeclaration(declaredName, null, null, null);
        declaredName.Parent = declaration;
        Assert.Same(declaration, GetDeclarationFromName(declaredName));
    }
}
