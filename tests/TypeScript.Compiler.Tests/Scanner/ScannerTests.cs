// ported from: internal/scanner/scanner_test.go（全部 3 个用例）
//
// 移植映射：
//   * Go 的表驱动子测试（tests := []struct{...}; t.Run(...)）→ xunit 的 [Theory] + [MemberData]（用例名进测试
//     输出，理由同 tests/TypeScript.Compiler.Tests/JsNum/JsNumTests.cs）。
//   * `TestScanStringPreservesLoneSurrogates` 的期望值里 Go 用 `stringutil.EncodeJSStringRune(0xD800)` 得到
//     "孤立代理项的 CESU-8 哨兵字符串"。C# 侧**不存在**这个函数（docs/architecture.md §3.1：C# 的 string 就是
//     UTF-16，孤立代理项天生合法），等价的写法就是原生 UTF-16 代理项字面量 `"\uD800"` —— 两者逐代码单元等值
//     （哨兵编码只是 Go 为绕开"UTF-8 不能表示孤立代理项"而做的中介层）。
//   * Go 直接 `&ast.Node{Kind: …, Flags: …, Loc: …}` 造节点；C# 的 Node 是抽象类，改成"具名节点类型 + 公开字段"
//     （`new JSDocAllType { Kind = …, Flags = …, Loc = … }`，见 docs/ast.md 的字段/工厂约定）。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Scanner;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;
using Xunit;
using Xunit.Abstractions;

namespace TypeScript.Tests;

public class ScannerTests(ITestOutputHelper output)
{
    [Fact]
    public void ScanStringPreservesLoneSurrogates()
    {
        var s = NewScanner();
        // Go 的原始字符串字面量 `"🦀\ud7ff\ud800\ud801\uD83E\uDD80"`：源码文本里是转义序列，不是真代理项。
        s.SetText("\"🦀\\ud7ff\\ud800\\ud801\\uD83E\\uDD80\"");

        Assert.Equal(Kind.StringLiteral, s.Scan());
        Assert.Equal(
            "🦀" +
            "\uD7FF" +   // Go: stringutil.EncodeJSStringRune(0xD7FF)
            "\uD800" +   // Go: stringutil.EncodeJSStringRune(0xD800)（孤立高代理项）
            "\uD801" +   // Go: stringutil.EncodeJSStringRune(0xD801)
            "🦀",
            s.TokenValue());
    }

    public static TheoryData<string, string[]> NormalizeJSDocTypeSourceTextTests => new()
    {
        { " \t* \tFoo", ["Foo"] },
        {
            "Foo\r\n * Bar\r\t* Baz\u2028 * Qux\u2029* Quux",
            ["Foo", "Bar", "Baz", "Qux", "Quux"]
        },
        { "Foo\r\n *\r\n", ["Foo", "", ""] },
        { "Foo\n  Bar", ["Foo", "Bar"] },
        { "**Foo", ["*Foo"] },
    };

    [Theory]
    [MemberData(nameof(NormalizeJSDocTypeSourceTextTests))]
    public void NormalizeJSDocTypeSourceText(string text, string[] expectedLines)
    {
        output.WriteLine($"normalizeJSDocTypeSourceText({text})");
        // Go: strings.Join(expectedLines, core.NewLineKindLF.GetNewLineCharacter())
        Assert.Equal(string.Join(NewLineKind.LF.GetNewLineCharacter(), expectedLines), TypeScript.Scanner.Scanner.NormalizeJSDocTypeSourceText(text));
    }

    [Fact]
    public void IsJSDocTypeExpressionOrChildWorks()
    {
        var jsDocType = new TypeReferenceNode { Kind = Kind.TypeReference, Flags = NodeFlags.JSDoc };
        var jsDocTypeChild = new Identifier { Kind = Kind.Identifier, Flags = NodeFlags.JSDoc, Parent = jsDocType };
        var reparsedType = new TypeLiteralNode { Kind = Kind.TypeLiteral, Flags = NodeFlags.Reparsed };
        var reparsedTypeChild = new Identifier { Kind = Kind.Identifier, Flags = NodeFlags.Reparsed, Parent = reparsedType };
        var ordinaryType = new TypeReferenceNode { Kind = Kind.TypeReference };
        var jsDocTag = new JSDocUnknownTag { Kind = Kind.JSDocParameterTag, Flags = NodeFlags.JSDoc };
        var jsDocTagChild = new Identifier { Kind = Kind.Identifier, Flags = NodeFlags.JSDoc, Parent = jsDocTag };

        (string Name, Node Node, bool Expected)[] tests =
        [
            ("type expression", new JSDocTypeExpression { Kind = Kind.JSDocTypeExpression }, true),
            ("JSDoc type", jsDocType, true),
            ("JSDoc type child", jsDocTypeChild, true),
            ("reparsed type", reparsedType, true),
            ("reparsed type child", reparsedTypeChild, true),
            ("ordinary type", ordinaryType, false),
            ("other JSDoc child", jsDocTagChild, false),
        ];

        foreach (var (name, node, expected) in tests)
        {
            output.WriteLine(name);
            Assert.Equal(expected, IsJSDocTypeExpressionOrChild(node));
        }
    }

    [Fact]
    public void GetTextOfNodeFromJSDocTypePreservesAsteriskType()
    {
        // Go: strings.Join([]string{"", " * *"}, LF)
        var sourceText = string.Join(NewLineKind.LF.GetNewLineCharacter(), "", " * *");
        var node = new JSDocAllType
        {
            Kind = Kind.JSDocAllType,
            Flags = NodeFlags.JSDoc,
            Loc = new TextRange(0, sourceText.Length),
        };

        Assert.Equal("*", GetTextOfNodeFromSourceText(sourceText, node, false /*includeTrivia*/));
    }
}
