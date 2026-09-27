// ported from: internal/astnav/tokens_test.go（+ testmain_test.go 的 baseline.Track 对应物不存在，见下）
//
// 与 Go 测试的差异（逐条）：
// - **node/JS 对拍部分没搬**：`baselineTokens` 里 `tsGetTokensAtPositions` / `tsGetTouchingPropertyName` /
//   `tsFindPrecedingTokens` 走 `jstest.EvalNodeScriptWithTS`（起 node + TS 的 emitter）比较 TS 与 Go 的输出，
//   以及 `baseline.Run`（写 baseline/NoContent 的测试基础设施）。这两样都属于 `tests/TypeScript.Harness`
//   （docs/harness.md），不在单测范围。这里保留**可单测的等价物**：
//     * Go 自己那侧的 JSON baseline（Go 的 "go baseline json" 子测试）：把 mapCode.ts 的每个位置扫一遍，
//       产出 `tokenRun` 数组，与 TSGO 已提交的 `testdata/baselines/reference/astnav/*.baseline.json` 逐字节比较。
//       这等于把 Go 侧行为钉在已提交的 baseline 上（无需 node）。
//     * `tokens_test.go` 里不依赖 node 的子测试（JSDoc type assertion ×2、pointer equality、TestUnitFindPrecedingToken）。
// - Go 的 `baselineGoTokensJSON` 里 `core.StringifyJson(runs, "", "  ")` → C# 的 `Core.StringifyJson`；
//   baseline 文件末尾多一个 "\n"（baseline.Run 加的），比较前去掉。
// - `toTokenInfo` 的 `strings.Replace(node.Kind.String(), "Kind", "", 1)` 逐字保留（C# 的 stringer 也产出
//   "KindXxx" 文本，见 KindStringer.Generated.cs），再把 "EndOfFile" 映射成 "EndOfFileToken"。
// - `FindNextToken` 的 baseline 生成器用 `recover()` 吞掉 panic（那些位置不会出现在 baseline 里）→ C# 用
//   `catch (Exception)` 复刻同一效果。
using JsonEncoder = TypeScript.Json.Encoder;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Json;
using TypeScript.TSPath;
using Xunit;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Core.Core;
using static TypeScript.Json.Json;
using static TypeScript.Parser.Parser;

namespace TypeScript.Tests;

public class AstNavTokensTests
{
    // docs/architecture.md §5：TSGO 仓库根目录由 TSGO_ROOT 指定，缺省探测 D:\Source\GitHub\TypeScript-Go。
    private static readonly string _tsgoRoot =
        Environment.GetEnvironmentVariable("TSGO_ROOT") is { Length: > 0 } root ? root : @"D:\Source\GitHub\TypeScript-Go";

    private static readonly string _testDataRoot = System.IO.Path.Combine(_tsgoRoot, "tsc", "testdata");

    // Go: var testFiles = []string{filepath.Join(repo.TestDataPath(), "fixtures/services/mapCode.ts")}
    private static readonly string _mapCodePath =
        System.IO.Path.Combine(_testDataRoot, "fixtures", "services", "mapCode.ts");

    private static SourceFile ParseTestFile(string fileText, ScriptKind scriptKind = ScriptKind.TS) =>
        ParseSourceFile(new SourceFileParseOptions { FileName = "/file.ts", Path = new TSPath.Path("/file.ts") }, fileText, scriptKind);

    [Fact]
    public void GetTokenAtPosition_JSDocTypeAssertion()
    {
        const string fileText = "function foo(x) {\n    const s = /**@type {string}*/(x)\n}";
        var file = ParseTestFile(fileText, ScriptKind.JS);

        // Position of 'x' inside the parenthesized expression (position 52)
        const int position = 52;

        // This should not panic - it previously panicked with:
        // "did not expect KindParenthesizedExpression to have KindIdentifier in its trivia"
        var token = GetTouchingPropertyName(file, position);
        Assert.NotNull(token);

        // The function may return either the identifier itself or the containing
        // parenthesized expression, depending on how the AST is structured
        Assert.True(
            token.Kind == Kind.Identifier || token.Kind == Kind.ParenthesizedExpression,
            $"Expected identifier or parenthesized expression, got {token.Kind}");
    }

    [Fact]
    public void GetTokenAtPosition_JSDocTypeAssertionWithComment()
    {
        // Exact code from the issue report
        const string fileText = "function foo(x) {\n    const s = /**@type {string}*/(x)  // Go-to-definition on x causes panic\n}";
        var file = ParseTestFile(fileText, ScriptKind.JS);

        // Find position of 'x' in the type assertion
        const int xPos = 52; // Position of 'x' in (x)

        // This should not panic
        var token = GetTouchingPropertyName(file, xPos);
        Assert.NotNull(token);
    }

    [Fact]
    public void GetTokenAtPosition_PointerEquality()
    {
        const string fileText = "\n\t\t\tfunction foo() {\n\t\t\t\treturn 0;\n\t\t\t}\n\t\t";
        var file = ParseTestFile(fileText);
        Assert.Same(GetTokenAtPosition(file, 0), GetTokenAtPosition(file, 0));
    }

    [Theory]
    [InlineData("GetTokenAtPosition")]
    [InlineData("GetTouchingPropertyName")]
    [InlineData("FindPrecedingToken")]
    public void MatchesCommittedGoTokenJsonBaseline(string testName)
    {
        if (!File.Exists(_mapCodePath))
        {
            return; // 需要 TSGO 检出（TSGO_ROOT），没有就跳过。
        }

        var fileText = File.ReadAllText(_mapCodePath);
        var baselinePath = System.IO.Path.Combine(_testDataRoot, "baselines", "reference", "astnav", $"{testName}.mapCode.ts.baseline.json");
        if (!File.Exists(baselinePath))
        {
            return;
        }

        Func<SourceFile, int, Node?> getToken = testName switch
        {
            "GetTokenAtPosition" => GetTokenAtPosition,
            "GetTouchingPropertyName" => GetTouchingPropertyName,
            "FindPrecedingToken" => (file, pos) => FindPrecedingToken(file, pos),
            _ => throw new InvalidOperationException(testName),
        };

        AssertBaselineJson(fileText, getToken, File.ReadAllText(baselinePath));
    }

    [Fact]
    public void FindNextToken_MatchesCommittedGoTokenJsonBaseline()
    {
        if (!File.Exists(_mapCodePath))
        {
            return; // 需要 TSGO 检出（TSGO_ROOT），没有就跳过。
        }

        var fileText = File.ReadAllText(_mapCodePath);
        var baselinePath = System.IO.Path.Combine(_testDataRoot, "baselines", "reference", "astnav", "FindNextToken.mapCode.ts.baseline.json");
        if (!File.Exists(baselinePath))
        {
            return;
        }

        AssertBaselineJson(fileText, (file, pos) =>
        {
            // FindNextToken panics (like Go's assert) when the scanner finds trivia between
            // previousToken.End() and the next syntactic token. Catch those to avoid crashing
            // the baseline generator;那些位置不会出现在 baseline 里。
            try
            {
                var token = GetTokenAtPosition(file, pos);
                return FindNextToken(token, file, file);
            }
            catch (Exception)
            {
                return null;
            }
        }, File.ReadAllText(baselinePath));
    }

    private static void AssertBaselineJson(string fileText, Func<SourceFile, int, Node?> getGoToken, string expectedBaseline)
    {
        var file = ParseTestFile(fileText);
        var maxPos = fileText.Length;
        var runs = new List<TokenRun>();
        TokenRun? current = null;

        for (var pos = 0; pos < maxPos; pos++)
        {
            var token = getGoToken(file, pos);
            var kind = token is null ? null : ToTokenInfoKind(token);
            if (current is not null && token is not null && current.Kind == kind && current.NodePos == token.Pos() && current.NodeEnd == token.End())
            {
                current.EndPos = pos;
            }
            else
            {
                if (current is not null)
                {
                    runs.Add(current);
                }

                if (token is not null)
                {
                    current = new TokenRun
                    {
                        StartPos = pos,
                        EndPos = pos,
                        Kind = kind!,
                        NodePos = token.Pos(),
                        NodeEnd = token.End(),
                    };
                }
                else
                {
                    current = null;
                }
            }
        }

        if (current is not null)
        {
            runs.Add(current);
        }

        var output = StringifyJson(runs, "", "  ");

        // baseline.Run 会补一个换行（Go 的 baseline.Run → "\n"）；逐字符比较，所以只去掉尾部换行。
        var expected = expectedBaseline.Replace("\r\n", "\n").TrimEnd('\n');
        if (!string.Equals(expected, output, StringComparison.Ordinal))
        {
            var diff = 0;
            while (diff < expected.Length && diff < output.Length && expected[diff] == output[diff])
            {
                diff++;
            }

            var from = Math.Max(0, diff - 60);
            throw new Xunit.Sdk.XunitException(
                $"token baseline mismatch at offset {diff} (expected {expected.Length} chars, actual {output.Length})\n" +
                $"expected: {expected[from..Math.Min(expected.Length, diff + 60)]}\n" +
                $"actual:   {output[from..Math.Min(output.Length, diff + 60)]}");
        }
    }

    // Go: toTokenInfo 的 kind 文本（"Kind" 前缀去掉一次 + EndOfFile → EndOfFileToken）
    private static string ToTokenInfoKind(Node node)
    {
        var kind = ReplaceFirst(node.Kind.String(), "Kind", "");
        if (kind == "EndOfFile")
        {
            kind = "EndOfFileToken";
        }

        return kind;
    }

    private static string ReplaceFirst(string text, string search, string replacement)
    {
        var index = text.IndexOf(search, StringComparison.Ordinal);
        return index < 0 ? text : text[..index] + replacement + text[(index + search.Length)..];
    }

    [Fact]
    public void UnitFindPrecedingToken_AfterDotInJsdoc()
    {
        const string fileContent = """
import {
    CharacterCodes,
    compareStringsCaseInsensitive,
    compareStringsCaseSensitive,
    compareValues,
    Comparison,
    Debug,
    endsWith,
    equateStringsCaseInsensitive,
    equateStringsCaseSensitive,
    GetCanonicalFileName,
    getDeclarationFileExtension,
    getStringComparer,
    identity,
    lastOrUndefined,
    Path,
    some,
    startsWith,
} from "./_namespaces/ts.js";

/**
 * Internally, we represent paths as strings with '/' as the directory separator.
 * When we make system calls (eg: LanguageServiceHost.getDirectory()),
 * we expect the host to correctly handle paths in our specified format.
 *
 * @internal
 */
export const directorySeparator = "/";
/** @internal */
export const altDirectorySeparator = "\\";
const urlSchemeSeparator = "://";
const backslashRegExp = /\\/g;


backslashRegExp.

//Path Tests

/**
 * Determines whether a charCode corresponds to '/' or '\'.
 *
 * @internal
 */
export function isAnyDirectorySeparator(charCode: number): boolean {
    return charCode === CharacterCodes.slash || charCode === CharacterCodes.backslash;
}
""";
        var file = ParseTestFile(fileContent);
        var token = FindPrecedingToken(file, 839);
        Assert.Equal(Kind.DotToken, token!.Kind);
    }

    [Fact]
    public void UnitFindPrecedingToken_AfterCommaInParameterList()
    {
        var file = ParseTestFile("takesCb((n, s, ))");
        var token = FindPrecedingToken(file, 15);
        Assert.Equal(Kind.CommaToken, token!.Kind);
    }

    /// <summary>Go 的 <c>type tokenRun struct</c>（字段顺序 = JSON 键顺序）。</summary>
    private sealed class TokenRun : IJsonMarshaler
    {
        public int StartPos;
        public int EndPos;
        public string Kind = "";
        public int NodePos;
        public int NodeEnd;

        public void MarshalJSONTo(JsonEncoder encoder)
        {
            encoder.WriteToken(BeginObject);
            WriteName(encoder, "startPos");
            MarshalEncode(encoder, StartPos);
            WriteName(encoder, "endPos");
            MarshalEncode(encoder, EndPos);
            WriteName(encoder, "kind");
            MarshalEncode(encoder, Kind);
            WriteName(encoder, "nodePos");
            MarshalEncode(encoder, NodePos);
            WriteName(encoder, "nodeEnd");
            MarshalEncode(encoder, NodeEnd);
            encoder.WriteToken(EndObject);
        }

        private static void WriteName(JsonEncoder encoder, string name) =>
            encoder.WriteToken(JsonToken.String(name));
    }
}
