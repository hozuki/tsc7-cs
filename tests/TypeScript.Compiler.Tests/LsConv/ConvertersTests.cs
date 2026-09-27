// ported from: internal/ls/lsconv/converters_test.go（380 行）+ converters.go 的行为
//
// 与 Go 的差异（逐条）：
// 1) `TestDocumentURIToFileName` 测的是 `DocumentUri.FileName()`（Go 里挂在 lsproto 上，但测试放在 lsconv 包）：
//    这里一并移植（LsProto 的测试没有覆盖 FileName）。
// 2) `TestConvertersInvalidUTF8` **不直接移植**：Go 的 string 是 UTF-8 字节序列，`"a\x80b\ncd"` 里的 `\x80`
//    是**非法 UTF-8 字节**，测试断言的是"非法字节只前进 1 而不被 RuneError 的 3 字节带偏"。C# 的 string 是
//    UTF-16 代码单元序列，`"\u0080"` 是合法字符（UTF-8 下 2 字节），该 bug 在 C# 侧不存在；改写成
//    "非 ASCII 往返 + 代理对不切开"的等价用例（见 ToLSPPositionAndBackRoundTrips*）。
// 3) `TestConvertersAgainstJSReference` 依赖 node 子进程、且核心是"用 Node 的 TextDecoder 语义校准 Go 的
//    UTF-16 换算"：C# 的内部坐标**本来就是** UTF-16 代码单元（= JS 字符串下标），该校准没有意义，故不移植；
//    代之以手工计算的期望表（覆盖 \n / \r / \r\n 与代理对）。
// 4) **新增**（Go 侧没有）：UTF-8 position encoding 的往返用例。这是本移植的**真实偏离点**（Go 在字节串上
//    做 `start+char`，C# 必须在 UTF-16 串上按码点累加 UTF-8 字节数），必须有确定性覆盖。
// 5) **新增**：`DiagnosticToLSPPull/Push` 的用例（Go 侧只被上游 ls/server 的集成测试间接覆盖）。用
//    `Ast.NewExternalDiagnostic` / `Diagnostic` 的内部构造器 + `LsProto.WithClientCapabilities` 固定客户端能力。
// 6) xunit 惯例：`t.Run` 的表驱动 → `[Theory]`/`[InlineData]`；`assert.Equal` → `Assert.Equal`；
//    `assert.Assert(t, a == b)` → `Assert.Same`（引用相等）或 `Assert.True`。
using System.Globalization;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.Parser;
using TypeScript.SpanMap;
using TypeScript.TSPath;
using Xunit;

using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.LsConv.LsConv;
using AstDiag = TypeScript.Ast.Diagnostic;
using LspDiag = TypeScript.LsProto.Diagnostic;
using Path = TypeScript.TSPath.Path;
using Range = TypeScript.LsProto.Range;
using SegmentKind = TypeScript.SpanMap.Kind;
using SpanMapType = TypeScript.SpanMap.SpanMap;
using TsParser = TypeScript.Parser.Parser;

namespace TypeScript.Tests;

public class ConvertersTests
{
    private sealed class TestScript(string name, string text, string originalText = "", SpanMapType? spanMap = null) : Script
    {
        public string FileName() => name;

        public string OriginalFileName() => name;

        public string Text() => text;

        public string OriginalText() => originalText != "" ? originalText : text;

        public SpanMapType? SpanMap() => spanMap;
    }

    private static (Converters Converters, TestScript Script) NewTestConverters(string text, PositionEncodingKind encoding = default)
    {
        var script = new TestScript("test.ts", text);
        var lineMap = ComputeLSPLineStarts(text);
        return (NewConverters(encoding, _ => lineMap), script);
    }

    private static SourceFile ParseTestFile(string text, string fileName = "/test.ts") =>
        TsParser.ParseSourceFile(
            new SourceFileParseOptions { FileName = fileName, Path = new Path(fileName) },
            text,
            ScriptKind.TS);

    /// <summary>
    /// `lsproto.Position` 在 C# 里是 class（无值语义相等），Go 的 `assert.Equal(pos1, pos2)` 在这里必须逐字段比。
    /// </summary>
    private static void AssertPosition(uint expectedLine, uint expectedCharacter, Position position)
    {
        Assert.Equal(expectedLine, position.Line);
        Assert.Equal(expectedCharacter, position.Character);
    }

    [Theory]
    [InlineData("/path/to/file.ts", "file:///path/to/file.ts")]
    [InlineData("//server/share/file.ts", "file://server/share/file.ts")]
    [InlineData("d:/work/tsgo932/lib/utils.ts", "file:///d%3A/work/tsgo932/lib/utils.ts")]
    [InlineData("D:/work/tsgo932/lib/utils.ts", "file:///d%3A/work/tsgo932/lib/utils.ts")]
    [InlineData("d:/work/tsgo932/app/(test)/comp/comp-test.tsx", "file:///d%3A/work/tsgo932/app/%28test%29/comp/comp-test.tsx")]
    [InlineData("c:/test/me", "file:///c%3A/test/me")]
    [InlineData("//shares/files/c#/p.cs", "file://shares/files/c%23/p.cs")]
    [InlineData(
        "c:/Source/Z\u00fcrich or Zurich (\u02c8zj\u028a\u0259r\u026ak,/Code/resources/app/plugins/c#/plugin.json",
        "file:///c%3A/Source/Z%C3%BCrich%20or%20Zurich%20%28%CB%88zj%CA%8A%C9%99r%C9%AAk%2C/Code/resources/app/plugins/c%23/plugin.json")]
    [InlineData("c:/test %/path", "file:///c%3A/test%20%25/path")]
    [InlineData("/", "file:///")]
    [InlineData("/_:/path", "file:///_%3A/path")]
    [InlineData("/users/me/c#-projects/", "file:///users/me/c%23-projects/")]
    [InlineData("//localhost/c$/GitDevelopment/express", "file://localhost/c%24/GitDevelopment/express")]
    [InlineData("c:/test with %25/c#code", "file:///c%3A/test%20with%20%2525/c%23code")]
    [InlineData("^/untitled/ts-nul-authority/Untitled-1", "untitled:Untitled-1")]
    [InlineData("^/untitled/ts-nul-authority/c:/Users/jrieken/Code/abc.txt", "untitled:c:/Users/jrieken/Code/abc.txt")]
    [InlineData(
        "^/untitled/ts-nul-authority///wsl%2Bubuntu/home/jabaile/work/TypeScript/newfile.ts",
        "untitled://wsl%2Bubuntu/home/jabaile/work/TypeScript/newfile.ts")]
    public void FileNameToDocumentURIPorts(string fileName, string expectedUri)
    {
        Assert.Equal(expectedUri, FileNameToDocumentURI(fileName).Value);
    }

    [Theory]
    [InlineData("file:///path/to/file.ts", "/path/to/file.ts")]
    [InlineData("file://server/share/file.ts", "//server/share/file.ts")]
    [InlineData("file:///d%3A/work/tsgo932/lib/utils.ts", "d:/work/tsgo932/lib/utils.ts")]
    [InlineData("file:///D%3A/work/tsgo932/lib/utils.ts", "D:/work/tsgo932/lib/utils.ts")]
    [InlineData("file:///d%3A/work/tsgo932/app/%28test%29/comp/comp-test.tsx", "d:/work/tsgo932/app/(test)/comp/comp-test.tsx")]
    [InlineData("file:///path/to/file.ts#section", "/path/to/file.ts")]
    [InlineData("file:///c:/test/me", "c:/test/me")]
    [InlineData("file://shares/files/c%23/p.cs", "//shares/files/c#/p.cs")]
    [InlineData(
        "file:///c:/Source/Z%C3%BCrich%20or%20Zurich%20(%CB%88zj%CA%8A%C9%99r%C9%AAk,/Code/resources/app/plugins/c%23/plugin.json",
        "c:/Source/Z\u00fcrich or Zurich (\u02c8zj\u028a\u0259r\u026ak,/Code/resources/app/plugins/c#/plugin.json")]
    [InlineData("file:///c:/test %25/path", "c:/test %/path")]
    [InlineData("file:///_:/path", "/_:/path")]
    [InlineData("file:///users/me/c%23-projects/", "/users/me/c#-projects/")]
    [InlineData("file://localhost/c%24/GitDevelopment/express", "//localhost/c$/GitDevelopment/express")]
    [InlineData("file:///c%3A/test%20with%20%2525/c%23code", "c:/test with %25/c#code")]
    [InlineData("untitled:Untitled-1", "^/untitled/ts-nul-authority/Untitled-1")]
    [InlineData("untitled:Untitled-1#fragment", "^/untitled/ts-nul-authority/Untitled-1#fragment")]
    [InlineData("untitled:c:/Users/jrieken/Code/abc.txt", "^/untitled/ts-nul-authority/c:/Users/jrieken/Code/abc.txt")]
    [InlineData("untitled:C:/Users/jrieken/Code/abc.txt", "^/untitled/ts-nul-authority/C:/Users/jrieken/Code/abc.txt")]
    [InlineData("untitled://wsl%2Bubuntu/home/jabaile/work/TypeScript/newfile.ts", "^/untitled/wsl%2Bubuntu/home/jabaile/work/TypeScript/newfile.ts")]
    public void DocumentUriFileNamePorts(string uri, string expectedFileName)
    {
        Assert.Equal(expectedFileName, new DocumentUri(uri).FileName());
    }

    [Theory]
    [InlineData("typescript", ScriptKind.TS)]
    [InlineData("typescriptreact", ScriptKind.TSX)]
    [InlineData("javascript", ScriptKind.JS)]
    [InlineData("javascriptreact", ScriptKind.JSX)]
    [InlineData("json", ScriptKind.JSON)]
    [InlineData("plaintext", ScriptKind.Unknown)]
    public void LanguageKindToScriptKindPorts(string languageId, ScriptKind expected)
    {
        Assert.Equal(expected, LanguageKindToScriptKind(new LanguageKind(languageId)));
    }

    [Theory]
    // "a\r\nb\rc\nd"：行首 [0,3,5,7]；UTF-16 代码单元下标 → (行, 列)
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(2, 0, 2)] // \n 属于第 0 行
    [InlineData(3, 1, 0)]
    [InlineData(4, 1, 1)]
    [InlineData(5, 2, 0)]
    [InlineData(7, 3, 0)]
    [InlineData(8, 3, 1)] // 文本末尾（EOF 在最后一个行首之后）
    public void ToLSPPositionAndBackRoundTripsUtf16(int offset, uint expectedLine, uint expectedCharacter)
    {
        var (converters, script) = NewTestConverters("a\r\nb\rc\nd", PositionEncodingKind.UTF16);

        var (position, fidelity) = converters.ToLSPPosition(script, new TextPos(offset));
        AssertPosition(expectedLine, expectedCharacter, position);
        Assert.Equal(Fidelity.Exact, fidelity);

        var positions = converters.FromLSPPosition(script, position, Feature.All);
        Assert.Single(positions);
        Assert.Equal(offset, positions[0].Position.Value);
        Assert.Equal(Fidelity.Exact, positions[0].Fidelity);
        Assert.Same(script, positions[0].Script);
    }

    [Fact]
    public void ToLSPPositionAndBackClampsToRuneBoundariesUtf16()
    {
        // 😀 占两个 UTF-16 代码单元；Go 的换算是"按码点走、不切进代理对"，落在对中间的列会退到一个码点边界。
        var (converters, script) = NewTestConverters("a\u03b1\U0001F600b\nc", PositionEncodingKind.UTF16);

        AssertPosition(0, 2, converters.ToLSPPosition(script, new TextPos(2)).Position);
        AssertPosition(0, 3, converters.ToLSPPosition(script, new TextPos(3)).Position);

        var clamped = converters.FromLSPPosition(script, new Position { Line = 0, Character = 3 }, Feature.All);
        Assert.Equal(2, Assert.Single(clamped).Position.Value); // 代理对中间 → 退到码点起点

        var boundaries = converters.FromLSPPosition(script, new Position { Line = 0, Character = 2 }, Feature.All);
        Assert.Equal(2, Assert.Single(boundaries).Position.Value);
    }

    [Theory]
    // "a\u03b1\U0001F600b\nc"：UTF-8 字节 → UTF-16 下标
    // 第 0 行 = "a"(1B) + "α"(2B) + "😀"(4B) + "b"(1B) = 8 字节；第 1 行 = "c"
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(2, 0, 3)]
    [InlineData(4, 0, 7)]
    [InlineData(5, 0, 8)] // 行尾（\n 之前）
    [InlineData(6, 1, 0)]
    [InlineData(7, 1, 1)] // EOF
    public void ToLSPPositionAndBackRoundTripsUtf8Encoding(int offset, uint expectedLine, uint expectedCharacter)
    {
        var (converters, script) = NewTestConverters("a\u03b1\U0001F600b\nc", PositionEncodingKind.UTF8);

        var (position, fidelity) = converters.ToLSPPosition(script, new TextPos(offset));
        AssertPosition(expectedLine, expectedCharacter, position);
        Assert.Equal(Fidelity.Exact, fidelity);

        var positions = converters.FromLSPPosition(script, position, Feature.All);
        Assert.Equal(offset, Assert.Single(positions).Position.Value);
    }

    [Fact]
    public void FromLSPPositionClampsUtf8OffsetToRuneBoundary()
    {
        var (converters, script) = NewTestConverters("a\u03b1\U0001F600b\nc", PositionEncodingKind.UTF8);

        // 字节 2 落在 α（字节 1..3）中间 → 退到该码点起点（UTF-16 下标 1）。
        var midRune = converters.FromLSPPosition(script, new Position { Line = 0, Character = 2 }, Feature.All);
        Assert.Equal(1, Assert.Single(midRune).Position.Value);
    }

    [Fact]
    public void ToLSPRangeAndLocationUseOriginalCoordinates()
    {
        const string text = "let x = 1;\nlet y = 2;\n";
        var (converters, script) = NewTestConverters(text);

        var (range, fidelity) = converters.ToLSPRange(script, NewTextRange(4, 5));
        Assert.Equal(Fidelity.Exact, fidelity);
        Assert.Equal(0u, range.Start.Line);
        Assert.Equal(4u, range.Start.Character);
        Assert.Equal(0u, range.End.Line);
        Assert.Equal(5u, range.End.Character);

        var (location, locationFidelity) = converters.ToLSPLocation(script, NewTextRange(12, 13));
        Assert.Equal(Fidelity.Exact, locationFidelity);
        Assert.Equal("file://test.ts", location.Uri.Value);
        Assert.Equal(1u, location.Range.Start.Line);
        Assert.Equal(1u, location.Range.Start.Character);
        Assert.Equal(2u, location.Range.End.Character);

        // 反向：LSP 区间 → 内部偏移（普通文件直接换算）。
        Assert.Equal(NewTextRange(4, 5), converters.FromLSPRangeToOriginal(script, range));
    }

    [Fact]
    public void FromLSPRangeForPlainScriptReturnsSingleExactSpan()
    {
        var (converters, script) = NewTestConverters("let x = 1;\n");

        var spans = converters.FromLSPRange(
            script,
            new Range { Start = new Position(), End = new Position { Line = 0, Character = 4 } },
            Feature.All);

        var mapped = Assert.Single(spans);
        Assert.Same(script, mapped.Script);
        Assert.Equal(NewTextRange(0, 4), mapped.Span);
        Assert.Equal(Fidelity.Exact, mapped.Fidelity);
    }

    [Fact]
    public void FromLSPRangeForContentMappedScriptMapsOriginalIntoVirtualAndFiltersFeatures()
    {
        // 原文 "abc" 在虚拟文本里是 "[abc]"：段 [1,4) ↔ 原文 [0,3)，只参与 Hover。
        var spanMap = SpanMapType.New([
            new Segment
            {
                VirtualStart = 1,
                VirtualEnd = 4,
                OriginalStart = 0,
                OriginalEnd = 3,
                Kind = SegmentKind.Verbatim,
                Features = Feature.Hover,
            },
        ]);
        var script = new TestScript("test.ts", "[abc]", originalText: "abc", spanMap);
        var converters = NewConverters(PositionEncodingKind.UTF16, _ => ComputeLSPLineStarts("abc"));
        var range = new Range { Start = new Position(), End = new Position { Line = 0, Character = 3 } };

        var mapped = Assert.Single(converters.FromLSPRange(script, range, Feature.Hover));
        Assert.Same(script, mapped.Script);
        Assert.Equal(NewTextRange(1, 4), mapped.Span);
        Assert.Equal(Fidelity.Exact, mapped.Fidelity);

        // 段不参与 Completion → 没有投影（Go 的 FromLSPRange 语义）。
        Assert.Empty(converters.FromLSPRange(script, range, Feature.Completion));
    }

    [Fact]
    public void ToLSPRangeMapsVirtualToOriginalAndReportsFidelity()
    {
        // 原文 "abc" 在虚拟文本里是 "[abc]"：段 [1,4) ↔ 原文 [0,3)，'['/']' 是合成文本。
        var spanMap = SpanMapType.New([
            new Segment
            {
                VirtualStart = 1,
                VirtualEnd = 4,
                OriginalStart = 0,
                OriginalEnd = 3,
                Kind = SegmentKind.Verbatim,
                Features = Feature.All,
            },
        ]);
        var script = new TestScript("test.ts", "[abc]", originalText: "abc", spanMap);
        var converters = NewConverters(PositionEncodingKind.UTF16, _ => ComputeLSPLineStarts("abc"));

        // 完全落在 verbatim 段内 → 精确映射回原文 [0,3)。
        var (exactRange, exactFidelity) = converters.ToLSPRange(script, NewTextRange(1, 4));
        Assert.Equal(Fidelity.Exact, exactFidelity);
        AssertPosition(0, 0, exactRange.Start);
        AssertPosition(0, 3, exactRange.End);

        // 跨出段（含合成的 '['）→ 端点被钳制，忠实度降为 Approximate（Go 的 VirtualToOriginalSpan）。
        var (partialRange, partialFidelity) = converters.ToLSPRange(script, NewTextRange(0, 4));
        Assert.Equal(Fidelity.Approximate, partialFidelity);
        AssertPosition(0, 0, partialRange.Start);
        AssertPosition(0, 3, partialRange.End);

        // 位置方向：段内的虚拟位置映射到原文位置。
        var (position, positionFidelity) = converters.ToLSPPosition(script, new TextPos(2));
        Assert.Equal(Fidelity.Exact, positionFidelity);
        AssertPosition(0, 1, position);
    }

    [Fact]
    public void FromLSPRangeForSourceFileExpandsProjections()
    {
        const string original = "x";
        var canonical = TsParser.ParseSourceFile(
            new SourceFileParseOptions { FileName = "/component.vue", Path = new Path("/component.vue") },
            " x",
            ScriptKind.TS);
        var supplemental = TsParser.ParseSourceFile(
            new SourceFileParseOptions { FileName = "/component.vue", Path = new Path("/component.vue::supplemental") },
            "  x",
            ScriptKind.TS);
        canonical.SetContentMapperInfo(new ContentMapperSourceFileInfo
        {
            OriginalText = original,
            ContentMapper = "mapper",
            SpanMap = SpanMapType.New([
                new Segment { VirtualStart = 1, VirtualEnd = 2, OriginalEnd = 1, Kind = SegmentKind.Verbatim, Features = Feature.All },
            ]),
            SupplementalSourceFiles = [supplemental],
        });
        supplemental.SetContentMapperInfo(new ContentMapperSourceFileInfo
        {
            OriginalText = original,
            ContentMapper = "mapper",
            SpanMap = SpanMapType.New([
                new Segment { VirtualStart = 2, VirtualEnd = 3, OriginalEnd = 1, Kind = SegmentKind.Verbatim, Features = Feature.All },
            ]),
            CanonicalSourceFile = canonical,
        });

        var converters = NewConverters(PositionEncodingKind.UTF16, _ => ComputeLSPLineStarts(original));

        var positions = converters.FromLSPPositionForSourceFile(canonical, new Position(), Feature.Hover);
        Assert.Equal(2, positions.Length);
        Assert.Same(canonical, positions[0].Script);
        Assert.Equal(1, positions[0].Position.Value);
        Assert.Equal(Fidelity.Exact, positions[0].Fidelity);
        Assert.Same(supplemental, positions[1].Script);
        Assert.Equal(2, positions[1].Position.Value);
        Assert.Equal(Fidelity.Exact, positions[1].Fidelity);
    }

    [Fact]
    public void FromLSPRangeIntersectingForSourceFileKeepsViewportIntersections()
    {
        const string original = "x";
        var canonical = TsParser.ParseSourceFile(
            new SourceFileParseOptions { FileName = "/component.vue", Path = new Path("/component.vue") },
            " x",
            ScriptKind.TS);
        var supplemental = TsParser.ParseSourceFile(
            new SourceFileParseOptions { FileName = "/component.vue", Path = new Path("/component.vue::supplemental") },
            "  x",
            ScriptKind.TS);
        canonical.SetContentMapperInfo(new ContentMapperSourceFileInfo
        {
            OriginalText = original,
            ContentMapper = "mapper",
            SpanMap = SpanMapType.New([
                new Segment { VirtualStart = 1, VirtualEnd = 2, OriginalEnd = 1, Kind = SegmentKind.Verbatim, Features = Feature.All },
            ]),
            SupplementalSourceFiles = [supplemental],
        });
        supplemental.SetContentMapperInfo(new ContentMapperSourceFileInfo
        {
            OriginalText = original,
            ContentMapper = "mapper",
            SpanMap = SpanMapType.New([
                new Segment { VirtualStart = 2, VirtualEnd = 3, OriginalEnd = 1, Kind = SegmentKind.Verbatim, Features = Feature.All },
            ]),
            CanonicalSourceFile = canonical,
        });

        var converters = NewConverters(PositionEncodingKind.UTF16, _ => ComputeLSPLineStarts(original));

        // 视口比映射出来的脚本段宽（两端都落在脚本之外），相交投影仍要全部保留。
        var spans = converters.FromLSPRangeIntersectingForSourceFile(
            canonical,
            new Range { Start = new Position(), End = new Position { Line = 0, Character = 1 } },
            Feature.Hover);
        Assert.Equal(2, spans.Length);
        Assert.Same(canonical, spans[0].Script);
        Assert.Equal(NewTextRange(1, 2), spans[0].Span);
        Assert.Same(supplemental, spans[1].Script);
        Assert.Equal(NewTextRange(2, 3), spans[1].Span);
    }

    [Fact]
    public void FromLSPRangeIntersectingForPlainFileReturnsWholeRange()
    {
        var (converters, script) = NewTestConverters("let x = 1;\n");
        var spans = converters.FromLSPRangeIntersectingForSourceFile(
            ParseTestFile("let x = 1;\n"),
            new Range { Start = new Position(), End = new Position { Line = 1, Character = 0 } },
            Feature.Hover);

        var mapped = Assert.Single(spans);
        Assert.Equal(NewTextRange(0, 11), mapped.Span);
        Assert.Equal(Fidelity.Exact, mapped.Fidelity);
        _ = script;
        _ = converters;
    }

    [Fact]
    public void DiagnosticToLSPPushMapsRangeSeverityCodeAndSource()
    {
        LsProto.LsProto.WithClientCapabilities(new ResolvedClientCapabilities());
        const string text = "let x = 1;\n";
        var file = ParseTestFile(text);
        var converters = NewConverters(PositionEncodingKind.UTF16, _ => ComputeLSPLineStarts(text));
        var diagnostic = NewExternalDiagnostic(
            file,
            NewTextRange(4, 5),
            "ts",
            Category.Warning,
            6133,
            "'x' is declared but never used.");

        var lspDiagnostic = DiagnosticToLSPPush(Context.Background, converters, diagnostic);

        Assert.Equal(0u, lspDiagnostic.Range.Start.Line);
        Assert.Equal(4u, lspDiagnostic.Range.Start.Character);
        Assert.Equal(0u, lspDiagnostic.Range.End.Line);
        Assert.Equal(5u, lspDiagnostic.Range.End.Character);
        Assert.Equal(DiagnosticSeverity.Warning, lspDiagnostic.Severity);
        Assert.Equal(6133, lspDiagnostic.Code!.Integer);
        Assert.Null(lspDiagnostic.Code!.String);
        Assert.Equal("ts", lspDiagnostic.Source);
        Assert.Equal("'x' is declared but never used.", lspDiagnostic.Message.String);
        Assert.Null(lspDiagnostic.Tags);
        Assert.Null(lspDiagnostic.RelatedInformation);
    }

    [Fact]
    public void DiagnosticToLSPPullUpgradesStyleChecksAndCarriesTagsAndRelatedInformation()
    {
        LsProto.LsProto.WithClientCapabilities(new ResolvedClientCapabilities
        {
            VSSupportsVisualStudioExtensions = true,
            TextDocument = new ResolvedTextDocumentClientCapabilities
            {
                Diagnostic = new ResolvedDiagnosticClientCapabilities
                {
                    RelatedInformation = true,
                    TagSupport = new ResolvedClientDiagnosticsTagOptions
                    {
                        ValueSet = [DiagnosticTag.Unnecessary, DiagnosticTag.Deprecated],
                    },
                },
            },
        });
        const string text = "let x = 1;\n";
        var file = ParseTestFile(text);
        var converters = NewConverters(PositionEncodingKind.UTF16, _ => ComputeLSPLineStarts(text));

        var related = new AstDiag(
            file,
            NewTextRange(0, 3),
            9999,
            Category.Message,
            "",
            null,
            "related message",
            default,
            null,
            null,
            null,
            false,
            false,
            false);
        var diagnostic = new AstDiag(
            file,
            NewTextRange(4, 5),
            6196,
            Category.Error,
            "",
            null,
            "'x' is declared but never used.",
            default,
            null,
            null,
            [related],
            true /* reportsUnnecessary */,
            false,
            false);

        var lspDiagnostic = DiagnosticToLSPPull(Context.Background, converters, diagnostic, reportStyleChecksAsWarnings: true);

        // 6196 在风格检查集合里：Error → Warning（vscode 的 styleCheckDiagnostics 行为）。
        Assert.Equal(DiagnosticSeverity.Warning, lspDiagnostic.Severity);
        // visualStudio 客户端用 "TS<code>" 字符串形式。
        Assert.Equal("TS6196", lspDiagnostic.Code!.String);
        Assert.Null(lspDiagnostic.Code!.Integer);
        Assert.Equal(new[] { DiagnosticTag.Unnecessary }, lspDiagnostic.Tags);

        var relatedInfo = lspDiagnostic.RelatedInformation![0]!;
        Assert.Single(lspDiagnostic.RelatedInformation!);
        Assert.Equal("file:///test.ts", relatedInfo.Location.Uri.Value);
        Assert.Equal(0u, relatedInfo.Location.Range.Start.Character);
        Assert.Equal(3u, relatedInfo.Location.Range.End.Character);
        Assert.Equal("related message", relatedInfo.Message);
    }

    [Fact]
    public void DiagnosticToLSPPullFallsBackToSourceTsWhenSourceIsEmpty()
    {
        LsProto.LsProto.WithClientCapabilities(new ResolvedClientCapabilities());
        const string text = "let x = 1;\n";
        var file = ParseTestFile(text);
        var converters = NewConverters(PositionEncodingKind.UTF16, _ => ComputeLSPLineStarts(text));
        var diagnostic = new AstDiag(
            file,
            NewTextRange(4, 5),
            1005,
            Category.Error,
            "",
            null,
            "Identifier expected.",
            default,
            null,
            null,
            null,
            false,
            false,
            false);

        var lspDiagnostic = DiagnosticToLSPPull(Context.Background, converters, diagnostic, reportStyleChecksAsWarnings: false);

        Assert.Equal("ts", lspDiagnostic.Source);
        Assert.Equal(DiagnosticSeverity.Error, lspDiagnostic.Severity);
        Assert.Equal(1005, lspDiagnostic.Code!.Integer);
        Assert.Equal("Identifier expected.", lspDiagnostic.Message.String);
        Assert.Equal(4u, lspDiagnostic.Range.Start.Character);
    }

    [Fact]
    public void DiagnosticWithoutFileUsesZeroRange()
    {
        LsProto.LsProto.WithClientCapabilities(new ResolvedClientCapabilities());
        var converters = NewConverters(PositionEncodingKind.UTF16, _ => ComputeLSPLineStarts(""));
        var diagnostic = NewCompilerDiagnostic(TypeScript.Diagnostics.Diagnostics.Cannot_find_name_0, "y");

        var lspDiagnostic = DiagnosticToLSPPush(Context.Background, converters, diagnostic);

        Assert.Equal(0u, lspDiagnostic.Range.Start.Line);
        Assert.Equal(0u, lspDiagnostic.Range.Start.Character);
        Assert.Equal(0u, lspDiagnostic.Range.End.Line);
        Assert.Equal(0u, lspDiagnostic.Range.End.Character);
        Assert.Contains("y", lspDiagnostic.Message.String!, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticCodeFormatsAsIntegerByDefault()
    {
        LsProto.LsProto.WithClientCapabilities(new ResolvedClientCapabilities());
        const string text = "let x = 1;\n";
        var file = ParseTestFile(text);
        var converters = NewConverters(PositionEncodingKind.UTF16, _ => ComputeLSPLineStarts(text));
        var diagnostic = NewExternalDiagnostic(file, NewTextRange(4, 5), "", Category.Suggestion, 80001, "hint");

        var lspDiagnostic = DiagnosticToLSPPush(Context.Background, converters, diagnostic);

        Assert.Equal(80001, lspDiagnostic.Code!.Integer);
        Assert.Null(lspDiagnostic.Code!.String);
        Assert.Equal(DiagnosticSeverity.Hint, lspDiagnostic.Severity);
        Assert.Equal("ts", lspDiagnostic.Source);
        Assert.Equal("80001", lspDiagnostic.Code!.Integer!.Value.ToString(CultureInfo.InvariantCulture));
    }
}
